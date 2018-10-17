using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Actors.Client;
using Lazlo.ShoppingSimulation.Common.Interfaces;
using System.Net.Http;
using Lazlo.Common.Models;
using Newtonsoft.Json;
using Lazlo.Common.Responses;
using Lazlo.ShoppingSimulation.Common;

namespace Lazlo.ShoppingSimulation.ConsumerExchangeActor
{
    /// <remarks>
    /// This class represents an actor.
    /// Every ActorID maps to an instance of this class.
    /// The StatePersistence attribute determines persistence and replication of actor state:
    ///  - Persisted: State is written to disk and replicated.
    ///  - Volatile: State is kept in memory only and replicated.
    ///  - None: State is kept in memory only and not replicated.
    /// </remarks>
    [StatePersistence(StatePersistence.Persisted)]
    public partial class ConsumerExchangeActor : Actor, IConsumerExchangeActor, IRemindable
    {
        const bool _UseLocalHost = false;
        string _UriBase = "devshopapi.services.32point6.com";

        const string ConsumerLicenseCodeKey = "ConsumerLicenseCodeKey";
        const string EntitiesKey = "EntitiesKey";

        const string WorkflowReminderKey = "WorkflowReminderKey";

        protected HttpClient _HttpClient = new HttpClient();

        /// <summary>
        /// Initializes a new instance of ConsumerExchangeActor
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public ConsumerExchangeActor(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {
        }

        /// <summary>
        /// This method is called whenever an actor is activated.
        /// An actor is activated the first time any of its methods are invoked.
        /// </summary>
        protected override Task OnActivateAsync()
        {
            ActorEventSource.Current.ActorMessage(this, "Actor activated.");

            return Task.CompletedTask;
        }

        private async Task<List<MerchantDisplay>> RetrieveMerchandiseAsync(Guid checkoutCorrelationRefId)
        {
            Uri requestUri = GetFullUri("api/v1/claim/ticket/exchange/merchandise");

            HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Get, requestUri);

            //httpreq.Headers.Add("Lazlo-SimulationLicenseCode", SimulationLicenseCode);
            //httpreq.Headers.Add("Lazlo-AuthorityLicenseCode", AuthorityLicenseCode);
            httpreq.Headers.Add("Lazlo-CorrelationRefId", checkoutCorrelationRefId.ToString());

            HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

            if (message.IsSuccessStatusCode)
            {
                string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

                var statusResponse = JsonConvert.DeserializeObject<SmartResponse<ClaimExchangeMerchantsResponse>>(responseJson);

                return statusResponse.Data.Merchandise;
            }

            else
            {
                string error = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

                throw new CorrelationException($"(Simulator Workflow) Player - Retrieve Merchandise Failed: {message.StatusCode} {error}") { CorrelationRefId = checkoutCorrelationRefId };
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

        public async Task InitializeAsync(string consumerLicenseCode, List<EntitySecret> entities)
        {
            await _StateMachine.FireAsync(_InitializeTrigger, consumerLicenseCode, entities);
        }

        private async Task OnInitialized(string consumerLicenseCode, List<EntitySecret> entities)
        {
            await RegisterReminderAsync(WorkflowReminderKey, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await StateManager.SetStateAsync(ConsumerLicenseCodeKey, consumerLicenseCode);
            await StateManager.SetStateAsync(EntitiesKey, entities);

            await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.GoIdle);
        }

        private async Task ValidateAsync()
        {
            
        }
    }
}
