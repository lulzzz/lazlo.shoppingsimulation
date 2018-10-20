using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lazlo.ShoppingSimulation.Common
{
    public class EntitySecret
    {
        [JsonProperty(PropertyName = "hash")]
        public string Hash { get; set; }

        [JsonProperty(PropertyName = "validationLicenseCode")]
        public string ValidationLicenseCode { get; set; }
    }
}
