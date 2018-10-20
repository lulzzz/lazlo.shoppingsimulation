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
using System.Diagnostics;

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

        const string AppApiLicenseCodeKey = "AppApiLicenseCodeKey";
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

        protected override async Task OnActivateAsync()
        {
            try
            {
                ConfigureStateMachine();

                if (_StateMachine.State == ConsumerSimulationExchangeState.None)
                {
                    await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.CreateActor);
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                throw;
            }
        }

        private async Task<List<MerchantDisplay>> RetrieveMerchandiseAsync()
        {
            Uri requestUri = GetFullUri("api/v1/claim/ticket/exchange/merchandise");

            HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Get, requestUri);

            string appApiLicenseCode = await StateManager.GetStateAsync<string>(AppApiLicenseCodeKey).ConfigureAwait(false);
            string consumerLicenseCode = await StateManager.GetStateAsync<string>(ConsumerLicenseCodeKey).ConfigureAwait(false);

            httpreq.Headers.Add("lazlo-consumerlicensecode", consumerLicenseCode);
            httpreq.Headers.Add("lazlo-apilicensecode", appApiLicenseCode);

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

                throw new Exception($"Error retrieving merchandise: {error}");
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

        public async Task InitializeAsync(string appApiLicenseCodeKey, string consumerLicenseCode, List<EntitySecret> entities)
        {
            await _StateMachine.FireAsync(_InitializeTrigger, appApiLicenseCodeKey, consumerLicenseCode, entities);
        }

        private async Task OnInitialized(string appApiLicenseCodeKey, string consumerLicenseCode, List<EntitySecret> entities)
        {
            await RegisterReminderAsync(WorkflowReminderKey, null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(5)).ConfigureAwait(false);

            await StateManager.SetStateAsync(AppApiLicenseCodeKey, appApiLicenseCodeKey);
            await StateManager.SetStateAsync(ConsumerLicenseCodeKey, consumerLicenseCode);
            await StateManager.SetStateAsync(EntitiesKey, entities);

            await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.GoIdle);

            WriteTimedDebug("Exchange Actor Initialized");
        }

        private async Task ValidateAsync()
        {
            try
            {
                //temp test, call validate first
                var merchandise = await RetrieveMerchandiseAsync();

                Debug.WriteLine($"Merchandise count: {merchandise.Count}");

                await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.GoIdle);
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);

                await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.GoIdle);
            }
        }

        private void WriteTimedDebug(string message)
        {
            Debug.WriteLine($"{DateTimeOffset.Now}: {message}");
        }

        private void WriteTimedDebug(Exception ex)
        {
            Debug.WriteLine($"{DateTimeOffset.Now}: {ex}");
        }
    }
}
