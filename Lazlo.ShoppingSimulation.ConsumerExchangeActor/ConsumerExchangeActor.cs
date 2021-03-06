﻿using System;
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
using Lazlo.Common.Requests;

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
        static readonly Uri EntityDownloadServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/ConsumerEntityDownloadActorService");

        const bool _UseLocalHost = false;
        string _UriBase = "devshopapi.services.32point6.com";

        const string AppApiLicenseCodeKey = "AppApiLicenseCodeKey";
        const string ConsumerLicenseCodeKey = "ConsumerLicenseCodeKey";
        const string EntitiesKey = "EntitiesKey";
        const string CheckoutSessionLicenseCodeKey = "CheckoutSessionLicenseCodeKey";
        const string ClaimLicenseCodeKey = "ClaimLicenseCodeKey";
        const string ClaimAmountKey = "ClaimAmountKey";
        const string PendingGiftCardsKey = "PendingGiftCardsKey";
        const string WorkflowReminderKey = "WorkflowReminderKey";

        const string WorkflowCompletionDetectedKey = "WorkflowCompletionDetectedKey";

        protected HttpClient _HttpClient = new HttpClient();

        double _Latitude = 42.129224;       //TODO pull from init
        double _Longitude = -80.085059;

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

        private async Task OnValidateAsync()
        {
            try
            {
                string appApiLicenseCode = await StateManager.GetStateAsync<string>(AppApiLicenseCodeKey).ConfigureAwait(false);
                string consumerLicenseCode = await StateManager.GetStateAsync<string>(ConsumerLicenseCodeKey).ConfigureAwait(false);
                List<EntitySecret> entities = await StateManager.GetStateAsync<List<EntitySecret>>(EntitiesKey).ConfigureAwait(false);

                Uri requestUri = GetFullUri("api/v2/validation/validate");

                HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

                SmartRequest<ValidationsRequest> validationRequest = new SmartRequest<ValidationsRequest>
                {
                    Data = new ValidationsRequest
                    {
                        Validations = entities.Select(z => new ValidationRequest
                        {
                            MediaHash = z.Hash,
                            ValidationLicenseCode = z.ValidationLicenseCode
                        }).ToList()
                    },
                    Latitude = _Latitude,
                    Longitude = _Longitude,
                    Uuid = Id.GetGuidId().ToString()
                };

                string json = JsonConvert.SerializeObject(validationRequest);

                httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                httpreq.Headers.Add("lazlo-consumerlicensecode", consumerLicenseCode);
                httpreq.Headers.Add("lazlo-apilicensecode", appApiLicenseCode);

                HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

                WriteTimedDebug($"Ticket Checkout request sent");

                string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

                var response = JsonConvert.DeserializeObject<SmartResponse<ValidationResponse>>(responseJson);

                if (message.IsSuccessStatusCode)
                {
                    if (response.Data.TicketValidationStatuses.Sum(z => z.CurrentValue) > 0)
                    {
                        if(await StateManager.ContainsStateAsync(WorkflowCompletionDetectedKey))
                        {
                            WriteTimedDebug("Houston, we've got a problem");
                        }

                        decimal claimAmount = response.Data.TicketValidationStatuses.Sum(z => z.CurrentValue);

                        await StateManager.SetStateAsync(ClaimLicenseCodeKey, response.Data.ClaimLicenseCode);
                        await StateManager.SetStateAsync(ClaimAmountKey, claimAmount);

                        await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.WaitToExchange);
                    }

                    else if(response.Data.TicketValidationStatuses.All(z => z.OutstandingPanelCount == 0) && response.Data.TicketValidationStatuses.Sum(z => z.CurrentValue) == 0)
                    {
                        if(await StateManager.TryAddStateAsync(WorkflowCompletionDetectedKey, true))
                        {
                            WriteTimedDebug($"Cycle complete: {Id}");
                        }

                        // Looping for now to make sure logic is correct, but I think I saw a potential situation where a ticket still had value 
                        await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.GoIdle);

                        // The following is ultimately the proper workflow
                        //var reminder = GetReminder(WorkflowReminderKey);
                        //await UnregisterReminderAsync(reminder);
                        //TODO Queue actor deletion
                    }

                    else
                    {
                        await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.GoIdle);
                    }
                }

                else
                {
                    throw new Exception($"Validation failed: {response.Error.Message}");
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.GoIdle);
            }
        }

        private async Task OnExchangeAsync()
        {
            try
            {
                string claimLicenseCode = await StateManager.GetStateAsync<string>(ClaimLicenseCodeKey);
                decimal totalAmount = await StateManager.GetStateAsync<decimal>(ClaimAmountKey);

                var availableMerchandise = await RetrieveMerchandiseAsync();

                // TODO Make this more robust
                MerchantDisplay merchant = (from p in availableMerchandise
                                            where p.Ranges.Any(z => totalAmount >= z.Low && totalAmount <= z.High)
                                            select p).First();

                string appApiLicenseCode = await StateManager.GetStateAsync<string>(AppApiLicenseCodeKey).ConfigureAwait(false);
                string consumerLicenseCode = await StateManager.GetStateAsync<string>(ConsumerLicenseCodeKey).ConfigureAwait(false);

                Uri requestUri = GetFullUri("api/v3/claim/ticket/exchange/low");

                HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

                SmartRequest<ClaimExchangeLowRequest> claimRequest = new SmartRequest<ClaimExchangeLowRequest>
                {
                    Data = new ClaimExchangeLowRequest
                    {
                        ClaimLicenseCode = claimLicenseCode,
                        Merchandise = new List<ClaimExchangeMerchandise>
                    {
                        new ClaimExchangeMerchandise
                        {
                            Amount = totalAmount,
                            MerchandiseRefId = merchant.MerchandiseRefId
                        }
                    }
                    },
                    Latitude = _Latitude,
                    Longitude = _Longitude,
                    Uuid = Id.GetGuidId().ToString()
                };

                string json = JsonConvert.SerializeObject(claimRequest);

                httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                httpreq.Headers.Add("lazlo-consumerlicensecode", consumerLicenseCode);
                httpreq.Headers.Add("lazlo-apilicensecode", appApiLicenseCode);

                HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

                string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

                var response = JsonConvert.DeserializeObject<SmartResponse<ClaimExchangeResponse2>>(responseJson);

                if (message.IsSuccessStatusCode)
                {
                    Debug.WriteLine("Exchange succeeded");

                    await StateManager.SetStateAsync(CheckoutSessionLicenseCodeKey, response.Data.CheckoutLicenseCode);

                    await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.WaitForGiftCardsToRender);
                }

                else
                {
                    throw new Exception($"Exchange failed: {response.Error.Message}");
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.WaitToExchange);
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
            await RegisterReminderAsync(WorkflowReminderKey, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            await StateManager.SetStateAsync(AppApiLicenseCodeKey, appApiLicenseCodeKey);
            await StateManager.SetStateAsync(ConsumerLicenseCodeKey, consumerLicenseCode);
            await StateManager.SetStateAsync(EntitiesKey, entities);

            await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.GoIdle);

            WriteTimedDebug("Exchange Actor Initialized");
        }

        private async Task OnCheckStatusAsync()
        {
            try
            {
                Uri requestUri = GetFullUri("api/v2/shopping/checkout/status");

                HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Get, requestUri);

                string appApiLicenseCode = await StateManager.GetStateAsync<string>(AppApiLicenseCodeKey).ConfigureAwait(false);
                string consumerLicenseCode = await StateManager.GetStateAsync<string>(ConsumerLicenseCodeKey).ConfigureAwait(false);
                string checkoutSessionLicenseCode = await StateManager.GetStateAsync<string>(CheckoutSessionLicenseCodeKey).ConfigureAwait(false);

                httpreq.Headers.Add("lazlo-consumerlicensecode", consumerLicenseCode);
                httpreq.Headers.Add("lazlo-apilicensecode", appApiLicenseCode);
                httpreq.Headers.Add("lazlo-txlicensecode", checkoutSessionLicenseCode);

                var message = await _HttpClient.SendAsync(httpreq);

                string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

                var statusResponse = JsonConvert.DeserializeObject<SmartResponse<CheckoutStatusResponse>>(responseJson);

                if (message.IsSuccessStatusCode)
                {
                    if(statusResponse.Data.GiftCardStatusCount == statusResponse.Data.GiftCardStatuses.Count(z => z.SasUri != null))
                    {
                        List<EntityDownload> downloads = new List<EntityDownload>();

                        foreach(var item in statusResponse.Data.GiftCardStatuses)
                        {
                            EntityDownload pendingDownload = new EntityDownload
                            {
                                MediaEntityType = Lazlo.Common.Enumerators.MediaEntityType.GiftCard,
                                MediaSize = item.MediaSize,
                                SasReadUri = item.SasUri,
                                ValidationLicenseCode = item.ValidationLicenseCode
                            };

                            downloads.Add(pendingDownload);

                            ActorId downloadActorId = new ActorId(pendingDownload.ValidationLicenseCode);

                            IConsumerEntityDownloadActor downloadActor = ActorProxy.Create<IConsumerEntityDownloadActor>(downloadActorId, EntityDownloadServiceUri);

                            await downloadActor.InitalizeAsync(appApiLicenseCode, checkoutSessionLicenseCode, ServiceUri, Id.GetGuidId(), consumerLicenseCode, pendingDownload);
                        }

                        await StateManager.SetStateAsync(PendingGiftCardsKey, downloads);

                        await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.WaitForGiftCardsToDownload);
                    }

                    else
                    {
                        await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.WaitForGiftCardsToRender);
                    }
                }

                else
                {
                    if(message.StatusCode == System.Net.HttpStatusCode.Processing)
                    {
                        await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.WaitForGiftCardsToRender);
                    }

                    else
                    {
                        throw new Exception(statusResponse.Error.Message);
                    }
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.WaitForGiftCardsToRender);
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

        public async Task UpdateDownloadStatusAsync(EntitySecret entitySecret)
        {
            List<EntityDownload> pending = await StateManager.GetStateAsync<List<EntityDownload>>(PendingGiftCardsKey);

            pending.RemoveAll(z => z.ValidationLicenseCode == entitySecret.ValidationLicenseCode);

            if(pending.Count == 0)
            {
                await StateManager.RemoveStateAsync(PendingGiftCardsKey);
                await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.GoIdle);
            }

            else
            {
                await StateManager.SetStateAsync(PendingGiftCardsKey, pending);
            }
        }
    }
}
