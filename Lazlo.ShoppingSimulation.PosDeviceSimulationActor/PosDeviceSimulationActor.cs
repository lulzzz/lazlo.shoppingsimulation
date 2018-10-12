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

        const bool _UseLocalHost = true;
        string _UriBase = "devshopapi.services.32point6.com";

        const string InitializedKey = "InitializedKey";
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
                Debug.WriteLine(ex);
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
                Debug.WriteLine(ex);
                throw;
            }
        }
               
        private async Task LogTransitionAsync(StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>.Transition arg)
        {
            Debug.WriteLine($"Transition from {arg.Source} to {arg.Destination}");

            try
            {
                //var conditionalValue = await this.StateManager.TryGetStateAsync<StateTransitionHistory<PosDeviceSimulationStateType>>("transitionHistory");

                //StateTransitionHistory<PosDeviceSimulationStateType> history;

                //if (conditionalValue.HasValue)
                //{
                //    history = StateTransitionHistory<PosDeviceSimulationStateType>.AddTransition(arg.Destination, conditionalValue.Value);
                //}
                //else
                //{
                //    history = new StateTransitionHistory<PosDeviceSimulationStateType>(arg.Destination);
                //}

                //await this.StateManager.SetStateAsync<StateTransitionHistory<PosDeviceSimulationStateType>>("transitionHistory", history);
            }

            catch (Exception ex)
            {
                Debug.WriteLine("Unable to save transition history");
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

        public async Task ProcessWorkflow()
        {
            Debug.WriteLine("PosDeviceSimulation ProcessWorkflowEntered");

            switch (_Machine.State)
            {
                case PosDeviceSimulationStateType.Idle:
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.CreateConsumer);
                    break;

                case PosDeviceSimulationStateType.CheckoutPendingQueued:
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.ProcessCheckoutPending);
                    break;
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
                Debug.WriteLine(ex);
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

        //private async Task OnInitialized(string appApiLicenseCode)
        //{
        //    this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.Idle;

        //    await SetStateAsync();

        //    ActorEventSource.Current.ActorMessage(this, $"Actor [{this.GetActorReference().ActorId.GetGuidId()}] initialized.");
        //}

        #region Register

        //private async Task OnRegistering()
        //{
        //    if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.Registering))
        //    {
        //        this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.Registering;

        //        await SetStateAsync();
        //    }
        //}

        //private async Task OnRegistered()
        //{
        //    if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.Registered))
        //    {
        //        this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.Registered;

        //        await SetStateAsync();
        //    }
        //}

        #endregion Cancel

        //#region TxStart

        //private Task<bool> CanTxStart(CancellationToken cancellationToken)
        //{
        //    Debug.Assert(state == _Machine.State);
        //    return Task.FromResult(_Machine.CanFire(PosDeviceSimulationTriggerType.TxStart));
        //}

        //private async Task OnTxStart(string appApiLicenseCode)
        //{
        //    if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.TxStarted))
        //    {
        //        this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.TxStarted;

        //        Debug.Assert(state == _Machine.State);
                
        //        // instantiate ConsumerActor and play a game






        //        await AddOrUpdateEntityStateAsync();

        //        //await this.machine.Activate(PosDeviceSimulationStateType.TxStarted;
        //    }
        //}

        //private async Task OnTxStarted()
        //{
        //    if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.Registered))
        //    {
        //        this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.Registered;

        //        Debug.Assert(state == _Machine.State);

        //        this._PosDevice.CreatedOn = DateTimeOffset.UtcNow;

        //        await AddOrUpdateEntityStateAsync();
        //    }
        //}

        //#endregion Cancel

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

        private static byte[] GetImageBytes(string imageName)
        {
            using (Stream stream = typeof(PosDeviceSimulationActor).Assembly.GetManifestResourceStream($"Lazlo.ShoppingSimulation.PosDeviceSimulationActor.Images.{imageName}"))
            {
                byte[] buffer = new byte[stream.Length];

                stream.Read(buffer, 0, buffer.Length);

                return buffer;
            }
        }

        private async Task CreateConsumerAsync()
        {
            byte[] selfieBytes = GetImageBytes("christina.png");

            string selfieBase64 = Convert.ToBase64String(selfieBytes);

            CryptoRandom random = new CryptoRandom();

            int age = random.Next(12, 115);

            SmartRequest<PlayerRegisterRequest> req = new SmartRequest<PlayerRegisterRequest>
            {
                CorrelationRefId = Guid.NewGuid(),
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

            Uri requestUri = GetFullUri("api/v3/player/registration");
            HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

            List<ApiLicenseDisplay> codes = await StateManager.GetStateAsync<List<ApiLicenseDisplay>>(AppApiLicensesKey);

            string appApiLicenseCode = codes.First().Code;

            //httpreq.Headers.Add("Lazlo-SimulationLicenseCode", SimulationLicenseCode);
            //httpreq.Headers.Add("Lazlo-AuthorityLicenseCode", AuthorityLicenseCode);
            httpreq.Headers.Add("lazlo-apilicensecode", appApiLicenseCode);
            httpreq.Headers.Add("lazlo-correlationrefId", req.CorrelationRefId.ToString());

            string json = JsonConvert.SerializeObject(req);

            httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

            string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (message.IsSuccessStatusCode)
            {
                if (age < 18)
                {
                    throw new CorrelationException("Allowed to register a player under 18") { CorrelationRefId = req.CorrelationRefId };
                }

                var statusResponse = JsonConvert.DeserializeObject<SmartResponse<ConsumerRegisterResponse>>(responseJson);

                ActorId consumerActorId = new ActorId(Guid.NewGuid());

                IConsumerSimulationActor consumerActor = ActorProxy.Create<IConsumerSimulationActor>(consumerActorId, ConsumerServiceUri);

                int modeSelection = random.Next(0, 2);

                await consumerActor.InitializeAsync(
                    appApiLicenseCode,
                    statusResponse.Data.ConsumerLicenseCode,
                    Id.GetGuidId(),
                    modeSelection == 0 ? PosDeviceModes.ConsumerScans : PosDeviceModes.PosDeviceScans).ConfigureAwait(false);

                await _Machine.FireAsync(PosDeviceSimulationTriggerType.WaitForConsumer);
            }

            else
            {
                // This will reset the state machine and cause the operation to be retried on the next loop
                await _Machine.FireAsync(PosDeviceSimulationTriggerType.GoIdle);

                //if (age >= 18)
                //{
                //    throw new CorrelationException($"Player registration failed: {message.StatusCode} {responseJson}") { CorrelationRefId = req.CorrelationRefId };
                //}

                //WriteTimedDebug("Player not registered due to age restriction");
            }
        }

        private void WriteTimedDebug(string message)
        {
            Debug.WriteLine($"{DateTimeOffset.Now}: {message}");
        }

        public async Task EnqueueCheckoutCompletePending(string checkoutSessionLicenseCode)
        {
            try
            {
                await _Machine.FireAsync(_CheckoutPendingTrigger, checkoutSessionLicenseCode);
            }

            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                throw;
            }
        }

        private async Task WaitingForConsumerToQueueCheckoutPending(string checkoutSessionLicenseCode)
        {
            await StateManager.SetStateAsync(CheckoutSessionLicenseCodeKey, checkoutSessionLicenseCode);
        }

        private async Task CheckoutCompletePendingAsync()
        {
            string checkoutSessionLicenseCode = await StateManager.GetStateAsync<string>(CheckoutSessionLicenseCodeKey);

            Uri requestUri = GetFullUri("api/v3/shopping/checkout/complete/pending");

            SmartRequest<CheckoutCompletePendingRequest> req = new SmartRequest<CheckoutCompletePendingRequest>
            {
                CreatedOn = DateTimeOffset.UtcNow,
                Data = new CheckoutCompletePendingRequest
                {
                    CheckoutSessionLicenseCode = checkoutSessionLicenseCode,
                },
                Latitude = 34.072846D,
                Longitude = -84.190285D,
                Uuid = "A8C1048F-5A2B-4953-9C71-36581827AFE1"   // Is this even used anymore?
            };

            string json = JsonConvert.SerializeObject(req);

            //List<ApiLicenseDisplay> codes = await StateManager.GetStateAsync<List<ApiLicenseDisplay>>(AppApiLicensesKey);

            //string appApiLicenseCode = codes.First().Code;

            HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

            string posDeviceApiLicenseCode = await StateManager.GetStateAsync<string>(PosDeviceApiLicenseCodeKey).ConfigureAwait(false);

            httpreq.Headers.Add("lazlo-apilicensecode", posDeviceApiLicenseCode);

            httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            DateTimeOffset opStart = DateTimeOffset.UtcNow;

            HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

            string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

            var response = JsonConvert.DeserializeObject<SmartResponse<CheckoutPendingResponse>>(responseJson);

            if (message.IsSuccessStatusCode)
            {
                Debug.WriteLine("Unlikely");
            }

            else
            {
                Debug.WriteLine("That's what I thought");
            }
        }

        public async Task<string> RetrieveCheckoutLicenseCode()
        {
            try
            {
                Uri requestUri = GetFullUri("api/v1/shopping/pos/checkout/create");

                HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

                string posDeviceApiLicenseCode = await StateManager.GetStateAsync<string>(PosDeviceApiLicenseCodeKey).ConfigureAwait(false);

                httpreq.Headers.Add("lazlo-apilicensecode", posDeviceApiLicenseCode);

                SmartRequest<object> req = new SmartRequest<object> { };

                string json = JsonConvert.SerializeObject(req);

                httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

                string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

                SmartResponse<CheckoutResponseBase> response = JsonConvert.DeserializeObject<SmartResponse<CheckoutResponseBase>>(responseJson);

                if (message.IsSuccessStatusCode)
                {
                    return response.Data.CheckoutLicenseCode;
                }

                else
                {
                    throw new Exception(response.Error.Message);
                }
            }

            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                throw;
            }
        }
    }
}
