using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Web;
using System.Web.Http;

namespace APIMiniCore.Controllers
{
    [RoutePrefix("api/accounts")]
    public class AccountsController : ApiController
    {
        private readonly string _connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        [HttpGet]
        [Route("{id}/balance")]
        public IHttpActionResult GetBalance(string id)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT Id, HolderName, Balance, [Status] FROM Accounts WHERE Id = @Id", conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read()) return NotFound();
                        return Ok(new
                        {
                            accountId = reader.GetString(0),
                            holderName = reader.GetString(1),
                            balance = reader.GetDecimal(2),
                            status = reader.GetString(3)
                        });
                    }
                }
            }
        }

        [HttpGet]
        [Route("{id}/movements")]
        public IHttpActionResult GetMovements(string id, [FromUri] DateTime? from = null, [FromUri] DateTime? to = null, [FromUri] int page = 1, [FromUri] int size = 20)
        {
            // F2: Validar tamaño máximo de página permitido
            if (size > 100) size = 100;
            if (page < 1) page = 1;
            int offset = (page - 1) * size;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Construcción dinámica parametrizada para evitar SQL Injection
                string query = @"
                    SELECT Id, TransferId, [Type], Amount, BalanceAfter, CreatedAtUtc 
                    FROM Movements WHERE AccountId = @AccId";

                if (from.HasValue) query += " AND CreatedAtUtc >= @From";
                if (to.HasValue) query += " AND CreatedAtUtc <= @To";

                query += " ORDER BY CreatedAtUtc DESC OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@AccId", id);
                    cmd.Parameters.AddWithValue("@Offset", offset);
                    cmd.Parameters.AddWithValue("@Size", size);
                    if (from.HasValue) cmd.Parameters.AddWithValue("@From", from.Value);
                    if (to.HasValue) cmd.Parameters.AddWithValue("@To", to.Value);

                    var list = new List<object>();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new
                            {
                                movementId = reader.GetInt64(0),
                                transferId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1),
                                type = GetMovementTypeName(reader.GetByte(2)),
                                amount = reader.GetDecimal(3),
                                balanceAfter = reader.GetDecimal(4),
                                createdAtUtc = reader.GetDateTime(5)
                            });
                        }
                    }
                    return Ok(list);
                }
            }
        }

        private string GetMovementTypeName(byte type)
        {
            switch (type)
            {
                case 1: return "Debit";
                case 2: return "Credit";
                case 3: return "Fee";
                case 4: return "ReversalDebit";
                case 5: return "ReversalCredit";
                case 6: return "FeeRefund";
                default: return "Unknown";
            }
        }
    }

}