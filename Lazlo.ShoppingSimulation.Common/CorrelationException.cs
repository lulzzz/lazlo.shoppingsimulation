using System;
using System.Collections.Generic;
using System.Text;

namespace Lazlo.ShoppingSimulation.Common
{
    public class CorrelationException : Exception
    {
        public CorrelationException(string message) : base(message)
        {

        }

        public Guid CorrelationRefId { get; set; }
    }
}
