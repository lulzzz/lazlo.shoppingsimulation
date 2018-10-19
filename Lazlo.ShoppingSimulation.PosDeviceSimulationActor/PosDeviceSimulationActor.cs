using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Actors.Client;
using Lazlo.ShoppingSimulation.Common.Interfaces;
using Lazlo.ShoppingSimulation.Common;
using System.Diagnostics;
using System.Net.Http;
using Newtonsoft.Json;
using Lazlo.Common.Responses;
using Lazlo.Common.Models;
using Lazlo.Common;
using System.IO;
using Lazlo.Gaming.Random;
using Lazlo.Common.Requests;
using Stateless;
using Lazlo.Common.Enumerators;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using Microsoft.ServiceFabric.Services.Client;

namespace Lazlo.ShoppingSimulation.PosDeviceSimulationActor
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
    public partial class PosDeviceSimulationActor : Actor, IPosDeviceSimulationActor, IRemindable
    {
        static readonly Uri ConsumerServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/ConsumerSimulationActorService");
        static readonly Uri LineServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/Lazlo.ShoppingSimulation.ConsumerLineService");

        const bool _UseLocalHost = false;
        string _UriBase = "devshopapi.services.32point6.com";

        const string CurrentConsumerIdKey = "CurrentConsumerIdKey";
        const string InitializedKey = "InitializedKey";
        const string CheckoutLicenseCodeKey = "CheckoutLicenseCodeKey";
        const string PosDeviceApiLicenseCodeKey = "PosDeviceApiLicenseCodeKey";
        const string PosDeviceModeKey = "PosDeviceModeKey";
        const string AppApiLicensesKey = "AppApiLicensesKey";
        const string StartupStatusKey = "StartupStatusKey";
        const string PlayerLicenseCodeKey = "PlayerLicenseCodeKey";
        const string KillMeReminderKey = "KillMeReminderKey";
        const string WorkflowReminderKey = "WorkflowReminderKey";
        const string StateKey = "StateKey";
        const string StateFlagsKey = "StateFlagsKey";

        const string CheckoutSessionLicenseCodeKey = "CheckoutSessionLicenseCodeKey";

        protected HttpClient _HttpClient = new HttpClient();

        double _Latitude = 42.129224;       //TODO pull from init
        double _Longitude = -80.085059;

        private PosDeviceSimulationStateType state;
        private PosDeviceSimulationStateType stateFlags;
        
        private StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>.TriggerWithParameters<string> txStartTrigger;

        /// <summary>
        /// Initializes a new instance of PosDeviceSimulationActor
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public PosDeviceSimulationActor(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {
            RegisterKillMeReminder().Wait();
        }

        /// <summary>
        /// This method is called whenever an actor is activated.
        /// An actor is activated the first time any of its methods are invoked.
        /// </summary>
        protected override async Task OnActivateAsync()
        {
            try
            {
                state = await StateManager.GetOrAddStateAsync<PosDeviceSimulationStateType>(StateKey, PosDeviceSimulationStateType.None);
                stateFlags = await StateManager.GetOrAddStateAsync<PosDeviceSimulationStateType>(StateKey, PosDeviceSimulationStateType.None);

                ConfigureMachine();

                if (_Machine.IsInState(PosDeviceSimulationStateType.None))
                {
                    // first load, initalize                    
                    await this._Machine.FireAsync(PosDeviceSimulationTriggerType.CreateActor);
                }

                ActorEventSource.Current.ActorMessage(this, $"Actor [{this.GetActorReference().ActorId.GetGuidId()}] activated.");

                ActorEventSource.Current.ActorMessage(this, $"Actor [{this.GetActorReference().ActorId.GetGuidId()}] state at activation: {this.state}");
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                throw;
            }
        }

        /// <summary>
        /// The Actor is Created by calling it, this initializes base values required to be an minimaly viable CheckoutSession
        /// </summary>
        /// <param name="authorityRefId"></param>
        /// <param name="brandRefId"></param>
        /// <param name="simulationType"></param>
        /// <returns></returns>
        public async Task InitializeAsync(string posDeviceApiLicenseCode, List<ApiLicenseDisplay> applicationLicenses, PosDeviceModes posDeviceModes)
        {
            try
            {
                if (_Machine.State == PosDeviceSimulationStateType.ActorCreated)
                {
                    await _Machine.FireAsync(_InitializeTrigger, posDeviceApiLicenseCode, applicationLicenses, posDeviceModes);
                }

                else
                {
                    ActorEventSource.Current.ActorMessage(this, $"{nameof(PosDeviceSimulationActor)} already initialized.");
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                throw;
            }
        }

        private async Task SetStateAsync()
        {
            await this.StateManager.SetStateAsync(StateKey, state);
            await this.StateManager.SetStateAsync(StateFlagsKey, stateFlags);
        }

        #region Reminder Management
        private async Task RegisterKillMeReminder()
        {
            try
            {
                IActorReminder reminderRegistration = await this.RegisterReminderAsync(
                    KillMeReminderKey,
                    null,
                    TimeSpan.FromMinutes(15),   //The amount of time to delay before firing the reminder
                    TimeSpan.FromMinutes(15)    //The time interval between firing of reminders
                    );

                //TODO telemetry
            }

            catch (Exception ex)
            {
                Debugger.Break();
                
            }
        }

        private async Task RegisterWorkflowReminder()
        {
            IActorReminder reminderRegistration = await this.RegisterReminderAsync(
                WorkflowReminderKey,
                null,
                TimeSpan.FromSeconds(5),   //The amount of time to delay before firing the reminder
                TimeSpan.FromSeconds(15)    //The time interval between firing of reminders
                );

            //TODO telemetry
        }

        private async Task<bool> ReminderUnRegistered(string reminderName)
        {
            try
            {
                IActorReminder reminder = GetReminder(reminderName);
                await UnregisterReminderAsync(reminder);
                return true;
            }
            catch (Exception ex)
            {
                //TODO telemetry
                return false;
            }
        }
        #endregion

        private async Task ProcessKillMe()
        {
            if (this._Machine.IsInState(PosDeviceSimulationStateType.DeadManWalking))
            {
                try
                {
                    var rnd = new Random();

                    //ICheckoutSessionManager proxy = ServiceProxy.Create<ICheckoutSessionManager>(
                    //    new Uri("fabric:/Deploy.Lazlo.Checkout.Api/Lazlo.SrvcFbrc.Services.CheckoutSessionManager"),
                    //    new Microsoft.ServiceFabric.Services.Client.ServicePartitionKey(
                    //            rnd.Next(0, 1023)));

                    //var killMe = await proxy.Archive(this.GetActorReference().ActorId.GetGuidId());
                }
                catch (Exception ex)
                {
                }
            }
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                ActorEventSource.Current.ActorMessage(this, $"{nameof(PosDeviceSimulationActor)} {Id} reminder recieved.");

                switch (reminderName)
                {
                    case KillMeReminderKey:
                        await ProcessKillMe();
                        break;

                    case WorkflowReminderKey:
                        await ProcessWorkflow();
                        break;
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
            }
        }

        #region Create

        private async Task OnCreated()
        {
            if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.ActorCreated))
            {
                // Only way to clean up the Actor Instance so do it first
                //await RegisterKillMeReminder();

                this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.ActorCreated;

                await SetStateAsync();
                await StateManager.SaveStateAsync();
            }
        }

        #endregion Create

        private async Task CreateToInitialized(string posDeviceApiLicenseCode, List<ApiLicenseDisplay> applicationLicenses, PosDeviceModes posDeviceModes)
        {
            await StateManager.TryAddStateAsync(PosDeviceApiLicenseCodeKey, posDeviceApiLicenseCode);
            await StateManager.TryAddStateAsync(AppApiLicensesKey, applicationLicenses);
            await StateManager.TryAddStateAsync(PosDeviceModeKey, posDeviceModes);

            await RegisterWorkflowReminder();

            await SetStateAsync();

            await _Machine.FireAsync(PosDeviceSimulationTriggerType.GoIdle);
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

        private async Task GetNextInLine()
        {
            try
            {
                WriteTimedDebug("GetNextInLine");

                Random random = new Random();

                PosDeviceModes posDeviceMode = random.Next(0, 2) == 0 ? PosDeviceModes.ConsumerScans : PosDeviceModes.PosDeviceScans;

                posDeviceMode = PosDeviceModes.PosDeviceScans;

                if (posDeviceMode == PosDeviceModes.ConsumerScans)
                {
                    await CreateCheckoutSessionAsync();
                }

                int partitionIndex = random.Next(0, 4);

                ServicePartitionKey servicePartitionKey = new ServicePartitionKey(partitionIndex);

                ILineService proxy = ServiceProxy.Create<ILineService>(LineServiceUri, servicePartitionKey);

                List<ApiLicenseDisplay> codes = await StateManager.GetStateAsync<List<ApiLicenseDisplay>>(AppApiLicensesKey);

                string appApiLicenseCode = codes.First().Code;

                Guid consumerId = await proxy.GetNextConsumerInLineAsync(appApiLicenseCode);

                await StateManager.SetStateAsync(CurrentConsumerIdKey, consumerId);

                ActorId consumerActorId = new ActorId(consumerId);

                IConsumerSimulationActor consumerActor = ActorProxy.Create<IConsumerSimulationActor>(consumerActorId, ConsumerServiceUri);

                await consumerActor.BeginTransaction(this.Id.GetGuidId(), posDeviceMode);

                if(posDeviceMode == PosDeviceModes.ConsumerScans)
                {
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.WaitForConsumerToCheckout);
                }

                else
                {
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.WaitForConsumerToPresentQr);
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                await _Machine.FireAsync(PosDeviceSimulationTriggerType.GoIdle);
            }
        }

        private async Task ScanConsumerQr()
        {
            try
            {
                Guid consumerId = await StateManager.GetStateAsync<Guid>(CurrentConsumerIdKey);

                ActorId consumerActorId = new ActorId(consumerId);

                IConsumerSimulationActor consumerActor = ActorProxy.Create<IConsumerSimulationActor>(consumerActorId, ConsumerServiceUri);

                string checkoutSessionLicenseCode = await consumerActor.PosScansConsumer().ConfigureAwait(false);

                if(checkoutSessionLicenseCode == null)
                {
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.WaitForConsumerToPresentQr);
                }

                else
                {
                    await StateManager.SetStateAsync(CheckoutLicenseCodeKey, checkoutSessionLicenseCode);

                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.WaitForConsumerToCheckout);
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                await _Machine.FireAsync(PosDeviceSimulationTriggerType.WaitForConsumerToPresentQr);
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

        private async Task GetLineItemsAsync()
        {
            try
            {
                WriteTimedDebug("CheckoutCompletePendingAsync");

                string checkoutSessionLicenseCode = await StateManager.GetStateAsync<string>(CheckoutLicenseCodeKey);

                Uri requestUri = GetFullUri("api/v1/shopping/pos/checkout/lineitems");

                HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Get, requestUri);

                string posDeviceApiLicenseCode = await StateManager.GetStateAsync<string>(PosDeviceApiLicenseCodeKey).ConfigureAwait(false);
                string checkoutSessionCode = await StateManager.GetStateAsync<string>(CheckoutLicenseCodeKey).ConfigureAwait(false);

                httpreq.Headers.Add("lazlo-txlicensecode", checkoutSessionCode);
                httpreq.Headers.Add("lazlo-apilicensecode", posDeviceApiLicenseCode);

                HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

                string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

                var response = JsonConvert.DeserializeObject<SmartResponse<CheckoutPendingResponse>>(responseJson);

                if (message.IsSuccessStatusCode)
                {
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.WaitForConsumerToPay);
                }

                else
                {
                    throw new Exception($"CheckoutCompletePending failed: {responseJson}");
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                await _Machine.FireAsync(PosDeviceSimulationTriggerType.WaitForConsumerToCheckout);
            }
        }

        public async Task CallCheckoutCompleteAsync()
        {
            try
            {
                Uri requestUri = GetFullUri("api/v2/shopping/pos/checkout/complete");

                SmartRequest<CheckoutCompletePosRequest> req = new SmartRequest<CheckoutCompletePosRequest>
                {
                    Data = new CheckoutCompletePosRequest
                    {
                        AmountPaid = 5.29M
                    }
                };

                HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Put, requestUri);

                string json = JsonConvert.SerializeObject(req);

                httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                string checkoutSessionCode = await StateManager.GetStateAsync<string>(CheckoutLicenseCodeKey).ConfigureAwait(false);
                string posDeviceApiLicenseCode = await StateManager.GetStateAsync<string>(PosDeviceApiLicenseCodeKey).ConfigureAwait(false);

                httpreq.Headers.Add("lazlo-apilicensecode", posDeviceApiLicenseCode);
                httpreq.Headers.Add("lazlo-txlicensecode", checkoutSessionCode);

                HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

                string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (message.IsSuccessStatusCode)
                {
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.WaitForConsumerToLeave);
                }

                else
                {
                    throw new Exception($"CheckoutComplete failed: {responseJson}");
                }
            }

            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                await _Machine.FireAsync(PosDeviceSimulationTriggerType.WaitForConsumerToPay);
            }
        }

        public async Task CreateCheckoutSessionAsync()
        {
            WriteTimedDebug("CreateCheckoutSessionAsync");

            Uri requestUri = GetFullUri("api/v1/shopping/pos/checkout/create");

            HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

            string posDeviceApiLicenseCode = await StateManager.GetStateAsync<string>(PosDeviceApiLicenseCodeKey).ConfigureAwait(false);

            httpreq.Headers.Add("lazlo-apilicensecode", posDeviceApiLicenseCode);

            SmartRequest<object> req = new SmartRequest<object> { };

            string json = JsonConvert.SerializeObject(req);

            httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

            string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

            SmartResponse<CheckoutSessionCreateResponse> response = JsonConvert.DeserializeObject<SmartResponse<CheckoutSessionCreateResponse>>(responseJson);

            if (message.IsSuccessStatusCode)
            {
                await StateManager.SetStateAsync(CheckoutLicenseCodeKey, response.Data.CheckoutLicenseCode);
            }

            else
            {
                throw new Exception(response.Error.Message);
            }
        }

        // In a sense this is outside of the workflow in the sense that the Pos doesn't know it has been scanned
        public async Task<string> ConsumerScansPos()
        {
            WriteTimedDebug("ConsumerScansPos");

            string checkoutLicenseCode = await StateManager.GetStateAsync<string>(CheckoutLicenseCodeKey).ConfigureAwait(false);

            return checkoutLicenseCode;
        }

        public async Task ConsumerLeavesPos()
        {
            await _Machine.FireAsync(PosDeviceSimulationTriggerType.ConsumerLeftLine);
        }
    }
}
