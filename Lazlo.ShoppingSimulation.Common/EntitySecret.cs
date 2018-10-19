using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lazlo.ShoppingSimulation.Common
{
    public class EntitySecret
    {
        [JsonProperty(PropertyName = "entityRefId")]
        public Guid EntityRefId { get; set; }

        [JsonProperty(PropertyName = "hash")]
        public string Hash { get; set; }

        [JsonProperty(PropertyName = "licenseCode")]
        public string LicenseCode { get; set; }

        public EntitySecret()
        {

        }
    }
}
