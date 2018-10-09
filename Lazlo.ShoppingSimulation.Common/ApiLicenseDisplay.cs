using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lazlo.ShoppingSimulation.Common
{
    public class ApiLicenseDisplay
    {
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }

        [JsonProperty(PropertyName = "code")]
        public string Code { get; set; }
    }

    public class SimulationConfiguration
    {
        [JsonProperty(PropertyName = "applicationLicenses")]
        public List<ApiLicenseDisplay> ApplicationLicenses { get; set; }

        [JsonProperty(PropertyName = "posDeviceLicenses")]
        public List<ApiLicenseDisplay> PosDeviceLicenses { get; set; }
    }
}
