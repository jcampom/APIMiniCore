using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;

namespace APIMiniCore.Models
{
    public class ProblemDetailsException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public string Code { get; }
        public string Title { get; }

        public ProblemDetailsException(HttpStatusCode statusCode, string code, string title) : base(title)
        {
            StatusCode = statusCode;
            Code = code;
            Title = title;
        }
    }

}