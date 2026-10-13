using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace APIMiniCore.Infrastructure
{
    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }
    }

}