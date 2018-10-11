using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Fabric;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lazlo.ShoppingSimulation.Common;
using Lazlo.ShoppingSimulation.Common.Interfaces;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Client;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lazlo.ShoppingSimulation.InitializationService
{
    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class InitializationService : StatelessService
    {
        static readonly Uri PosServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/PosDeviceSimulationActorService");

        public InitializationService(StatelessServiceContext context)
            : base(context)
        {

        }

        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                string configurationUri = "https://devlng.blob.core.windows.net/simulation/SimulationConfiguration.json?st=2018-10-09T20%3A46%3A59Z&se=2028-10-10T20%3A46%3A00Z&sp=rl&sv=2018-03-28&sr=b&sig=c12%2Fj4Zjk0ci5PnzoC15kQ2OxzHMbeoWysfARdsg0eQ%3D";

                string jsonConfig;

                using (HttpClient client = new HttpClient())
                {
                    jsonConfig = await client.GetStringAsync(configurationUri);
                }

                SimulationConfiguration config = JsonConvert.DeserializeObject<SimulationConfiguration>(jsonConfig);

                foreach (ApiLicenseDisplay apiLicenseDisplay in config.PosDeviceLicenses)
                {
                    ActorId posActorId = new ActorId(apiLicenseDisplay.Id);

                    IPosDeviceSimulationActor posDeviceActor = ActorProxy.Create<IPosDeviceSimulationActor>(posActorId, PosServiceUri);

                    await posDeviceActor.InitializeAsync(apiLicenseDisplay.Code, config.ApplicationLicenses, PosDeviceModes.PosDeviceScans);

                    await posDeviceActor.InitializeAsync(apiLicenseDisplay.Code, config.ApplicationLicenses, PosDeviceModes.PosDeviceScans);

                    break;
                }
            }

            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }
}
