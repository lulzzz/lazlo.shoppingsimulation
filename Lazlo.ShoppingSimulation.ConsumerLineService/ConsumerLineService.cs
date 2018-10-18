using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lazlo.ShoppingSimulation.Common.Interfaces;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Data;
using System.Net.Http;
using Newtonsoft.Json;
using System.IO;
using Lazlo.Gaming.Random;
using Lazlo.Common.Requests;
using Lazlo.ShoppingSimulation.Common;
using Lazlo.Common.Responses;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Client;
using System.Diagnostics;

namespace Lazlo.ShoppingSimulation.ConsumerLineService
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class ConsumerLineService : StatefulService, ILineService
    {
        const bool _UseLocalHost = false;
        string _UriBase = "devshopapi.services.32point6.com";

        const string ConsumerQueueName = "ConsumerQueueName";
        static readonly Uri ConsumerServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/ConsumerSimulationActorService");

        private HttpClient _HttpClient = new HttpClient();

        public ConsumerLineService(StatefulServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Optional override to create listeners (e.g., HTTP, Service Remoting, WCF, etc.) for this service replica to handle client or user requests.
        /// </summary>
        /// <remarks>
        /// For more information on service communication, see https://aka.ms/servicefabricservicecommunication
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return this.CreateServiceRemotingReplicaListeners();
        }

        public Task GetInLineAsync(Guid consumerActorId)
        {
            throw new NotImplementedException();
        }

        public async Task<Guid> GetNextConsumerInLineAsync(string appApiLicenseCode)
        {
            try
            {
                IReliableQueue<Guid> queue = await StateManager.GetOrAddAsync<IReliableQueue<Guid>>(ConsumerQueueName).ConfigureAwait(false);

                using (ITransaction tx = StateManager.CreateTransaction())
                {
                    ConditionalValue<Guid> queueItem = await queue.TryDequeueAsync(tx);

                    if(queueItem.HasValue)
                    {
                        await tx.CommitAsync();

                        return queueItem.Value;
                    }
                }

                Guid actorId = await CreateConsumerAsync(appApiLicenseCode).ConfigureAwait(false);

                return actorId;
            }

            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Debugger.Break();
                throw;
            }
        }

        private async Task<Guid> CreateConsumerAsync(string appApiLicenseCode)
        {
            byte[] selfieBytes = GetImageBytes("christina.png");

            string selfieBase64 = Convert.ToBase64String(selfieBytes);

            CryptoRandom random = new CryptoRandom();

            int age = random.Next(22, 115);

            SmartRequest<PlayerRegisterRequest> req = new SmartRequest<PlayerRegisterRequest>
            {
                CreatedOn = DateTimeOffset.UtcNow,
                Latitude = 34.072846D,
                Longitude = -84.190285D,
                Data = new PlayerRegisterRequest
                {
                    CountryCode = "US",
                    LanguageCode = "en-US",
                    SelfieBase64 = selfieBase64,
                    Data = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("age", age.ToString())
                    }
                }
                ,
                Uuid = $"{Guid.NewGuid()}"
            };

            Guid correlationRefId = Guid.NewGuid();

            Uri requestUri = GetFullUri("api/v3/player/registration");
            HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

            httpreq.Headers.Add("lazlo-apilicensecode", appApiLicenseCode);
            httpreq.Headers.Add("lazlo-correlationrefId", correlationRefId.ToString());

            string json = JsonConvert.SerializeObject(req);

            httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

            string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (message.IsSuccessStatusCode)
            {
                if (age < 18)
                {
                    throw new CorrelationException("Allowed to register a player under 18") { CorrelationRefId = correlationRefId };
                }

                var statusResponse = JsonConvert.DeserializeObject<SmartResponse<ConsumerRegisterResponse>>(responseJson);

                ActorId consumerActorId = new ActorId(Guid.NewGuid());

                IConsumerSimulationActor consumerActor = ActorProxy.Create<IConsumerSimulationActor>(consumerActorId, ConsumerServiceUri);

                await consumerActor.InitializeAsync(
                    appApiLicenseCode,
                    statusResponse.Data.ConsumerLicenseCode).ConfigureAwait(false);

                return consumerActorId.GetGuidId();
            }

            else
            {
                throw new Exception("Create player failed");
            }
        }

        private static byte[] GetImageBytes(string imageName)
        {
            using (Stream stream = typeof(ConsumerLineService).Assembly.GetManifestResourceStream($"Lazlo.ShoppingSimulation.ConsumerLineService.Images.{imageName}"))
            {
                byte[] buffer = new byte[stream.Length];

                stream.Read(buffer, 0, buffer.Length);

                return buffer;
            }
        }

        private Uri GetFullUri(string fragment)
        {
            if (_UseLocalHost)
            {
                return new Uri($"http://localhost:8343/{fragment}");
            }

            else
            {
                return new Uri($"http://{_UriBase}/{fragment}");
            }
        }
    }
}
