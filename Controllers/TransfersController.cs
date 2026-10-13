using APIMiniCore.Infrastructure;
using APIMiniCore.Models;
using Newtonsoft.Json;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Http;

namespace APIMiniCore.Controllers
{
    [RoutePrefix("api/transfers")]
    public class TransfersController : ApiController
    {
        private readonly string _connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
        private readonly IDateTimeProvider _timeProvider = new SystemDateTimeProvider();

        [HttpPost]
        [Route("")]
        public IHttpActionResult CreateTransfer([FromBody] TransferRequest request)
        {
            string correlationId = HttpContext.Current.Request.Headers["X-Correlation-Id"] ?? Guid.NewGuid().ToString();
            HttpContext.Current.Response.Headers.Add("X-Correlation-Id", correlationId);

            string idempotencyKeyStr = HttpContext.Current.Request.Headers["Idempotency-Key"];
            if (string.IsNullOrEmpty(idempotencyKeyStr) || !Guid.TryParse(idempotencyKeyStr, out Guid idempotencyKey))
            {
                return Content(HttpStatusCode.BadRequest, new { code = "MISSING_IDEMPOTENCY_KEY", title = "Falta la cabecera Idempotency-Key" });
            }

            if (request == null || request.Amount <= 0 || (request.Amount * 100) % 1 != 0 || request.Currency != "USD")
            {
                return Content((HttpStatusCode)422, new { code = "INVALID_AMOUNT", title = "Monto inválido o moneda no soportada" });
            }

            if (request.SourceAccountId == request.DestinationAccountId)
            {
                return Content((HttpStatusCode)422, new { code = "SAME_ACCOUNT", title = "Las cuentas origen y destino deben ser distintas" });
            }

            string currentRequestHash = ComputeRequestHash(request);

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var idemCmd = new SqlCommand("SELECT ResponseStatus, ResponseBody, RequestHash FROM IdempotencyKeys WHERE [Key] = @Key", conn))
                {
                    idemCmd.Parameters.AddWithValue("@Key", idempotencyKey);
                    using (var reader = idemCmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var status = (HttpStatusCode)reader.GetInt32(0);
                            var body = reader.GetString(1);
                            var savedHash = reader.GetString(2);

                            if (savedHash.Trim() != currentRequestHash.Trim())
                            {
                                return Content((HttpStatusCode)422, new { code = "IDEMPOTENCY_KEY_REUSED", title = "Misma clave con payload distinto." });
                            }

                            HttpContext.Current.Response.Headers.Add("Idempotent-Replayed", "true");
                            return Content(status, JsonConvert.DeserializeObject(body));
                        }
                    }
                }

                using (var transaction = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        DateTime now = _timeProvider.UtcNow;

                        SaveIdempotency(idempotencyKey, "PostmanClient", currentRequestHash, (int)HttpStatusCode.Created, "PENDING", now, conn, transaction);

                        Account sourceAcc = GetAccountForUpdate(request.SourceAccountId, conn, transaction);
                        Account destAcc = GetAccountForUpdate(request.DestinationAccountId, conn, transaction);

                        if (sourceAcc == null || destAcc == null)
                            throw new ProblemDetailsException(HttpStatusCode.NotFound, "ACCOUNT_NOT_FOUND", "Alguna de las cuentas no existe");

                        if (sourceAcc.Status != "Activa" || destAcc.Status != "Activa")
                            throw new ProblemDetailsException((HttpStatusCode)422, "ACCOUNT_NOT_ACTIVE", "La cuenta origen o destino no se encuentra activa");

                        decimal fee = 0.00m;
                        if (request.Amount > 500.00m)
                        {
                            fee = Math.Round(request.Amount * 0.005m, 2, MidpointRounding.AwayFromZero);
                            if (fee < 3.00m) fee = 3.00m;
                        }

                        decimal totalDebited = request.Amount + fee;

                        if (sourceAcc.Balance < totalDebited)
                            throw new ProblemDetailsException((HttpStatusCode)422, "INSUFFICIENT_FUNDS", "Saldo insuficiente para cubrir el monto más la comisión");

                        decimal dailyAccumulated = GetDailyAccumulated(request.SourceAccountId, now.Date, conn, transaction);
                        if (dailyAccumulated + request.Amount > 5000.00m)
                            throw new ProblemDetailsException((HttpStatusCode)422, "DAILY_LIMIT_EXCEEDED", "Se superaría el límite de transferencia diario permitido de 5000.00 USD");

                        UpdateAccountBalance(request.SourceAccountId, sourceAcc.Balance - totalDebited, sourceAcc.RowVersion, conn, transaction);
                        UpdateAccountBalance(request.DestinationAccountId, destAcc.Balance + request.Amount, destAcc.RowVersion, conn, transaction);

                        long transferId;
                        using (var tCmd = new SqlCommand(@"
                            INSERT INTO Transfers (SourceAccountId, DestinationAccountId, Amount, Fee, [Status], CreatedAtUtc, IdempotencyKey)
                            VALUES (@Src, @Dst, @Amt, @Fee, 1, @Created, @IdemKey); SELECT SCOPE_IDENTITY();", conn, transaction))
                        {
                            tCmd.Parameters.AddWithValue("@Src", request.SourceAccountId);
                            tCmd.Parameters.AddWithValue("@Dst", request.DestinationAccountId);
                            tCmd.Parameters.AddWithValue("@Amt", request.Amount);
                            tCmd.Parameters.AddWithValue("@Fee", fee);
                            tCmd.Parameters.AddWithValue("@Created", now);
                            tCmd.Parameters.AddWithValue("@IdemKey", idempotencyKey);
                            transferId = Convert.ToInt64(tCmd.ExecuteScalar());
                        }

                        InsertMovement(request.SourceAccountId, transferId, 1, request.Amount, sourceAcc.Balance - request.Amount, now, conn, transaction);
                        if (fee > 0)
                            InsertMovement(request.SourceAccountId, transferId, 3, fee, sourceAcc.Balance - totalDebited, now, conn, transaction);

                        InsertMovement(request.DestinationAccountId, transferId, 2, request.Amount, destAcc.Balance + request.Amount, now, conn, transaction);

                        var responseObj = new
                        {
                            transferId = transferId,
                            status = "Completed",
                            amount = request.Amount,
                            fee = fee,
                            totalDebited = totalDebited,
                            sourceBalanceAfter = sourceAcc.Balance - totalDebited,
                            createdAtUtc = now
                        };

                        string jsonResponseFinal = JsonConvert.SerializeObject(responseObj);

                        using (var updateIdemCmd = new SqlCommand("UPDATE IdempotencyKeys SET ResponseBody = @Body WHERE [Key] = @Key", conn, transaction))
                        {
                            updateIdemCmd.Parameters.AddWithValue("@Body", jsonResponseFinal);
                            updateIdemCmd.Parameters.AddWithValue("@Key", idempotencyKey);
                            updateIdemCmd.ExecuteNonQuery();
                        }

                        SaveAuditLog(now, "SYSTEM", "Transfer_Initiated", transferId.ToString(), correlationId, "Transferencia procesada con exito", conn, transaction);

                        transaction.Commit();
                        return Content(HttpStatusCode.Created, responseObj);
                    }
                    catch (ProblemDetailsException ex)
                    {
                        transaction.Rollback();
                        return Content(ex.StatusCode, new { code = ex.Code, title = ex.Message });
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return Content(HttpStatusCode.InternalServerError, new { title = "Error critico", error = ex.Message });
                    }
                }
            }
        }
        [HttpPost]
        [Route("{id}/reverse")]
        public IHttpActionResult ReverseTransfer(long id)
        {
            string correlationId = HttpContext.Current.Request.Headers["X-Correlation-Id"] ?? Guid.NewGuid().ToString();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        DateTime now = _timeProvider.UtcNow;

                        Transfer tx = GetTransferForUpdate(id, conn, transaction);
                        if (tx == null) return NotFound();

                        if (tx.Status == 2)
                            throw new ProblemDetailsException(HttpStatusCode.Conflict, "TRANSFER_ALREADY_REVERSED", "El reverso ya se realizo");

                        if ((now - tx.CreatedAtUtc).TotalHours > 24)
                            throw new ProblemDetailsException((HttpStatusCode)422, "REVERSAL_WINDOW_EXPIRED", "Ventana de 24h expirada");

                        Account sourceAcc = GetAccountForUpdate(tx.SourceAccountId, conn, transaction);
                        Account destAcc = GetAccountForUpdate(tx.DestinationAccountId, conn, transaction);

                        if (destAcc.Balance < tx.Amount)
                            throw new ProblemDetailsException(HttpStatusCode.Conflict, "REVERSAL_INSUFFICIENT_FUNDS", "Destino sin saldo suficiente");

                        decimal totalToRefund = tx.Amount + tx.Fee;

                        UpdateAccountBalance(tx.SourceAccountId, sourceAcc.Balance + totalToRefund, sourceAcc.RowVersion, conn, transaction);
                        UpdateAccountBalance(tx.DestinationAccountId, destAcc.Balance - tx.Amount, destAcc.RowVersion, conn, transaction);

                        using (var upTx = new SqlCommand("UPDATE Transfers SET [Status] = 2, ReversedAtUtc = @RevAt WHERE Id = @Id", conn, transaction))
                        {
                            upTx.Parameters.AddWithValue("@RevAt", now);
                            upTx.Parameters.AddWithValue("@Id", id);
                            upTx.ExecuteNonQuery();
                        }

                        InsertMovement(tx.SourceAccountId, id, 4, tx.Amount, sourceAcc.Balance + tx.Amount, now, conn, transaction);
                        if (tx.Fee > 0)
                            InsertMovement(tx.SourceAccountId, id, 6, tx.Fee, sourceAcc.Balance + totalToRefund, now, conn, transaction);

                        InsertMovement(tx.DestinationAccountId, id, 5, tx.Amount, destAcc.Balance - tx.Amount, now, conn, transaction);

                        SaveAuditLog(now, "OPERATOR_USER", "Transfer_Reversed", id.ToString(), correlationId, "Reverso exitoso", conn, transaction);

                        transaction.Commit();
                        return Ok(new { message = "Transferencia reversada correctamente" });
                    }
                    catch (ProblemDetailsException ex)
                    {
                        transaction.Rollback();
                        return Content(ex.StatusCode, new { code = ex.Code, title = ex.Message });
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return Content(HttpStatusCode.InternalServerError, new { title = "Error critico reverso", error = ex.Message });
                    }
                }
            }
        }

        private string ComputeRequestHash(TransferRequest req)
        {
            if (req == null) return string.Empty;
            string rawData = $"{req.SourceAccountId}|{req.DestinationAccountId}|{req.Amount:F2}|{req.Currency}";
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private Account GetAccountForUpdate(string accountId, SqlConnection conn, SqlTransaction tx)
        {
            using (var cmd = new SqlCommand("SELECT Id, [Status], Balance, RowVersion FROM Accounts WITH (UPDLOCK) WHERE Id = @Id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@Id", accountId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new Account { Id = accountId, Status = reader.GetString(1), Balance = reader.GetDecimal(2), RowVersion = (byte[])reader.GetValue(3) };
                }
            }
        }

        private Transfer GetTransferForUpdate(long id, SqlConnection conn, SqlTransaction tx)
        {
            using (var cmd = new SqlCommand("SELECT Id, SourceAccountId, DestinationAccountId, Amount, Fee, [Status], CreatedAtUtc FROM Transfers WITH (UPDLOCK) WHERE Id = @Id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new Transfer { Id = reader.GetInt64(0), SourceAccountId = reader.GetString(1), DestinationAccountId = reader.GetString(2), Amount = reader.GetDecimal(3), Fee = reader.GetDecimal(4), Status = reader.GetByte(5), CreatedAtUtc = reader.GetDateTime(6) };
                }
            }
        }

        private decimal GetDailyAccumulated(string accountId, DateTime dateUtc, SqlConnection conn, SqlTransaction tx)
        {
            using (var cmd = new SqlCommand("SELECT ISNULL(SUM(Amount), 0) FROM Transfers WHERE SourceAccountId = @Src AND [Status] = 1 AND CreatedAtUtc >= @Start AND CreatedAtUtc < @End", conn, tx))
            {
                cmd.Parameters.AddWithValue("@Src", accountId);
                cmd.Parameters.AddWithValue("@Start", dateUtc);
                cmd.Parameters.AddWithValue("@End", dateUtc.AddDays(1));
                return (decimal)cmd.ExecuteScalar();
            }
        }

        private void UpdateAccountBalance(string accountId, decimal newBalance, byte[] oldRowVersion, SqlConnection conn, SqlTransaction tx)
        {
            using (var cmd = new SqlCommand("UPDATE Accounts SET Balance = @Balance WHERE Id = @Id AND RowVersion = @RV", conn, tx))
            {
                cmd.Parameters.AddWithValue("@Balance", newBalance);
                cmd.Parameters.AddWithValue("@Id", accountId);
                cmd.Parameters.AddWithValue("@RV", oldRowVersion);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0) throw new Exception("Error de concurrencia optimista.");
            }
        }

        private void InsertMovement(string accountId, long transferId, byte type, decimal amount, decimal balanceAfter, DateTime created, SqlConnection conn, SqlTransaction tx)
        {
            using (var cmd = new SqlCommand("INSERT INTO Movements (AccountId, TransferId, [Type], Amount, BalanceAfter, CreatedAtUtc) VALUES (@Acc, @Tx, @Type, @Amt, @After, @Created)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@Acc", accountId);
                cmd.Parameters.AddWithValue("@Tx", transferId);
                cmd.Parameters.AddWithValue("@Type", type);
                cmd.Parameters.AddWithValue("@Amt", amount);
                cmd.Parameters.AddWithValue("@After", balanceAfter);
                cmd.Parameters.AddWithValue("@Created", created);
                cmd.ExecuteNonQuery();
            }
        }

        private void SaveIdempotency(Guid key, string clientId, string hash, int status, string body, DateTime created, SqlConnection conn, SqlTransaction tx)
        {
            using (var cmd = new SqlCommand("INSERT INTO IdempotencyKeys ([Key], ClientId, RequestHash, ResponseStatus, ResponseBody, CreatedAtUtc) VALUES (@Key, @Client, @Hash, @Status, @Body, @Created)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@Key", key);
                cmd.Parameters.AddWithValue("@Client", clientId);
                cmd.Parameters.AddWithValue("@Hash", hash);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@Body", body);
                cmd.Parameters.AddWithValue("@Created", created);
                cmd.ExecuteNonQuery();
            }
        }

        private void SaveAuditLog(DateTime now, string actor, string action, string entity, string corr, string detail, SqlConnection conn, SqlTransaction tx)
        {
            using (var cmd = new SqlCommand("INSERT INTO AuditLog (TimestampUtc, ActorId, Action, EntityId, CorrelationId, Details) VALUES (@Time, @Actor, @Act, @Entity, @Corr, @Detail)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@Time", now);
                cmd.Parameters.AddWithValue("@Actor", actor);
                cmd.Parameters.AddWithValue("@Act", action);
                cmd.Parameters.AddWithValue("@Entity", entity);
                cmd.Parameters.AddWithValue("@Corr", Guid.Parse(corr));
                cmd.Parameters.AddWithValue("@Detail", detail);
                cmd.ExecuteNonQuery();
            }
        }
    }

    public class TransferRequest { public string SourceAccountId { get; set; } public string DestinationAccountId { get; set; } public decimal Amount { get; set; } public string Currency { get; set; } }
    public class Account { public string Id; public string Status; public decimal Balance; public byte[] RowVersion; }
    public class Transfer { public long Id; public string SourceAccountId; public string DestinationAccountId; public decimal Amount; public decimal Fee; public byte Status; public DateTime CreatedAtUtc; }
}