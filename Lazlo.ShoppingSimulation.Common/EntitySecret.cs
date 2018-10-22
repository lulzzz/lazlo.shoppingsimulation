using Lazlo.Common.Enumerators;
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

        [JsonProperty(PropertyName = "mediaEntityType")]
        public MediaEntityType MediaEntityType { get; set; }

        public EntitySecret()
        {

        }
    }

    public class EntityDownload
    {
        [JsonProperty(PropertyName = "mediaEntityType")]
        public MediaEntityType MediaEntityType { get; set; }

        [JsonProperty(PropertyName = "mediaSize")]
        public long MediaSize { get; set; }

        [JsonProperty(PropertyName = "sasReadUri")]
        public string SasReadUri { get; set; }

        [JsonProperty(PropertyName = "validationLicenseCode")]
        public string ValidationLicenseCode { get; set; }

        public EntityDownload()
        {

        }
    }
}
