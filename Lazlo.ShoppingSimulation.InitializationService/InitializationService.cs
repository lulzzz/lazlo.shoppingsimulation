using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Fabric;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lazlo.ShoppingSimulation.Common.Interfaces;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Client;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Newtonsoft.Json.Linq;

namespace Lazlo.ShoppingSimulation.InitializationService
{
    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class InitializationService : StatelessService
    {
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
                string code;

                using (HttpClient client = new HttpClient())
                {
                    code = await client.GetStringAsync("https://devlng.blob.core.windows.net/simulation/PosDeviceLicenseCodes.json?st=2017-10-09T02%3A55%3A00Z&se=2028-10-10T02%3A55%3A00Z&sp=rl&sv=2018-03-28&sr=b&sig=uFCkCVp2zN2trKUJY2nxLozNfzB9%2B%2BlNTHKqkFSCfg0%3D");
                }

                JArray codes = JArray.Parse(code);

                foreach (JObject jo in codes)
                {
                    Guid licenseCode = (Guid)jo["id"];

                    //ActorId partitionActorId = new ActorId(Guid.Parse("0515122e-337e-47da-aa84-2b1f5b6e5d23"));

                    //IPosDeviceSimulationActor posDeviceActor = ActorProxy.Create<IPosDeviceSimulationActor>(partitionActorId, new Uri("fabric:/LotteryNG.ServiceFabric.Simulation/PlayerActorService"));

                    //await posDeviceActor.InitializeAsync();
                }


            }

            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }
}
