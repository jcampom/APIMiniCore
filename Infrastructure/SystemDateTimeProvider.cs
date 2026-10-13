using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace APIMiniCore.Infrastructure
{
    public class SystemDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}