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
    public enum PosDeviceSimulationTriggerType
    {
        // reserved
        Create = 0,
        Initialize = 1,
        // reserved

        Registering = 16,
        Registered = 32,
        TxStart = 64,
        TxRelease = 128, // idempotent
        TxProcess = 512, // hand to ConsumerActor
        TxEnded = 1024, // Actor reports back I'm done
    }

    [Flags]
    public enum PosDeviceSimulationStateType
    {
        None = 0,
        Created = 1,
        Initialized = 2,
        Registering = 16,
        Registered = 32,
        TxStarted = 64,
        TxReleasing = 128, // idempotent
        TxReleased = 256, // succeeeded in calling checkoutcomplete
        TxProcessing = 512, // hand to ConsumerActor
        TxEnded = 1024, // Actor reports back I'm done
        DeadManWalking = 8196
    }

    /// <remarks>
    /// This class represents an actor.
    /// Every ActorID maps to an instance of this class.
    /// The StatePersistence attribute determines persistence and replication of actor state:
    ///  - Persisted: State is written to disk and replicated.
    ///  - Volatile: State is kept in memory only and replicated.
    ///  - None: State is kept in memory only and not replicated.
    /// </remarks>
    [StatePersistence(StatePersistence.Persisted)]
    internal class PosDeviceSimulationActor : Actor, IPosDeviceSimulationActor, IRemindable
    {
        static readonly Uri ConsumerServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/ConsumerSimulationActorService");

        const bool _UseLocalHost = false;
        string _UriBase = "devshopapi.services.32point6.com";

        const string InitializedKey = "InitializedKey";
        const string LicenseCodeKey = "LicenseCodeKey";
        const string PosDeviceModeKey = "PosDeviceModeKey";
        const string ReminderName = "ReminderName";
        const string StartupStatusKey = "StartupStatusKey";
        const string PlayerLicenseCodeKey = "PlayerLicenseCodeKey";

        protected HttpClient _HttpClient = new HttpClient();

        //const string RetailerRefIdKey = "RetailerRefIdKey"; //TODO Pull in as part of Initialization

        double _Latitude = 42.129224;       //TODO pull from init
        double _Longitude = -80.085059;
        










        private PosDeviceSimulationStateType state;
        private PosDeviceSimulationStateType stateFlags;
        private StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType> machine;
        private StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>.TriggerWithParameters<string> initializedTrigger;
        private StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>.TriggerWithParameters<string> txStartTrigger;

        private PosDevice _PosDevice { get; set; }

        /// <summary>
        /// Initializes a new instance of PosDeviceSimulationActor
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public PosDeviceSimulationActor(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {   
            // This is a critical step
            // We have no way to clean up Actors without this call
            // Enumerating the services Actors is no realistic as it will have millions in production
            if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.Created))
            {
                // Only way to clean up the Actor Instance so do it first
                RegisterKillMeReminder().Wait();
            }
        }

        /// <summary>
        /// This method is called whenever an actor is activated.
        /// An actor is activated the first time any of its methods are invoked.
        /// </summary>
        protected override async Task OnActivateAsync()
        {
            try
            {
                var savedState = await this.StateManager.TryGetStateAsync<PosDeviceSimulationStateType>($"{nameof(this.state)}");
                var savedStateFlags = await this.StateManager.TryGetStateAsync<PosDeviceSimulationStateType>($"{nameof(this.stateFlags)}");

                ConfigureMachine();
                
                if (this.machine.IsInState(PosDeviceSimulationStateType.None))
                {
                    // first load, initalize                    
                    await this.machine.FireAsync(PosDeviceSimulationTriggerType.Create);
                }

                //TODO wrap in Polly                
                var tryGetEntity = StateManager.TryGetStateAsync<string>($"{nameof(PosDevice)}").Result;
                if (tryGetEntity.HasValue) this._PosDevice = JsonConvert.DeserializeObject<PosDevice>(tryGetEntity.Value);

                ActorEventSource.Current.ActorMessage(this, $"Actor [{this.GetActorReference().ActorId.GetGuidId()}] activated.");

                ActorEventSource.Current.ActorMessage(this, $"Actor [{this.GetActorReference().ActorId.GetGuidId()}] state at activation: {this.state}");
            }
            catch (Exception ex)
            {
            }

            return;
        }

        /// <summary>
        /// The Actor is Created by calling it, this initializes base values required to be an minimaly viable CheckoutSession
        /// </summary>
        /// <param name="authorityRefId"></param>
        /// <param name="brandRefId"></param>
        /// <param name="simulationType"></param>
        /// <returns></returns>
        public async Task InitializeAsync(string posDeviceApiLicenseCode, List<ApiLicenseDisplay> ApplicationLicenses, PosDeviceModes posDeviceModes)
        {
            if (this.machine.IsInState(PosDeviceSimulationStateType.Created))
            {
                await this.machine.FireAsync(PosDeviceSimulationTriggerType.Initialize);

                //await this.machine.FireAsync(PosDeviceSimulationTriggerType.Registering);

                //await this.machine.FireAsync(initializedTrigger, posDeviceApiLicenseCode);
            }

            else
            {

            }

            ActorEventSource.Current.ActorMessage(this, $"{nameof(PosDeviceSimulationActor)} already initialized.");
            throw new InvalidOperationException($"{nameof(PosDeviceSimulationActor)} [{this.GetActorReference().ActorId.GetGuidId()}] has already been initialized.");
        }

        private void ConfigureMachine()
        {
            machine = new StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>(
                () => this.state,
                s => this.state = s
                );

            // this state machine code is modified from https://github.com/dotnet-state-machine/stateless/tree/dev/example/BugTrackerExample
            // under the Apache 2.0 license: https://github.com/dotnet-state-machine/stateless/blob/dev/LICENSE
            machine.OnTransitionedAsync(LogTransitionAsync<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>);

            machine.Configure(PosDeviceSimulationStateType.None)
                .Permit(PosDeviceSimulationTriggerType.Create, PosDeviceSimulationStateType.Created)
                ;

            machine.Configure(PosDeviceSimulationStateType.Created)
                .Permit(PosDeviceSimulationTriggerType.Initialize, PosDeviceSimulationStateType.Initialized)
                .OnEntryAsync(async () => await OnCreated())
                ;

            //initializedTrigger = machine.SetTriggerParameters<string>(PosDeviceSimulationTriggerType.Initialize);
            machine.Configure(PosDeviceSimulationStateType.Initialized)
                .Permit(PosDeviceSimulationTriggerType.Registering, PosDeviceSimulationStateType.Registering)
                .Permit(PosDeviceSimulationTriggerType.Create, PosDeviceSimulationStateType.Created)
                .OnEntryAsync(CatchAll)
                //.OnEntryFromAsync(PosDeviceSimulationTriggerType.Registering, OtherToInitialized)
                .OnEntryFromAsync(PosDeviceSimulationTriggerType.Initialize, CreateToInitialized)
                //.OnEntryFromAsync(initializedTrigger, (appApiLicenseCode) => OnInitialized(appApiLicenseCode))
                ;

            machine.Configure(PosDeviceSimulationStateType.Registering)
                .SubstateOf(PosDeviceSimulationStateType.Initialized)
                .OnEntryAsync(async () => await OnRegistering())
                //.OnExitAsync(() => OnDeadManWalking())
                ;

            machine.Configure(PosDeviceSimulationStateType.Registered)
                .SubstateOf(PosDeviceSimulationStateType.Initialized)
                .OnEntryAsync(async () => await OnRegistered())
                //.OnExitAsync(() => OnDeadManWalking())
                ;

            txStartTrigger = machine.SetTriggerParameters<string>(PosDeviceSimulationTriggerType.TxStart);
            machine.Configure(PosDeviceSimulationStateType.TxStarted)
                .Permit(PosDeviceSimulationTriggerType.TxRelease, PosDeviceSimulationStateType.TxReleased)
                .OnEntryFromAsync(txStartTrigger, (appApiLicenseCode) => OnTxStart(appApiLicenseCode))
                ;
        }

        private async Task LogTransitionAsync<T, K>(
            StateMachine<T,
            K>.Transition arg
            )
        {
            var conditionalValue = await this.StateManager.TryGetStateAsync<StateTransitionHistory<T>>("transitionHistory");

            StateTransitionHistory<T> history;
            if (conditionalValue.HasValue)
            {
                history = StateTransitionHistory<T>.AddTransition(arg.Destination, conditionalValue.Value);
            }
            else
            {
                history = new StateTransitionHistory<T>(arg.Destination);
            }

            await this.StateManager.SetStateAsync<StateTransitionHistory<T>>("transitionHistory", history);
        }

        private async Task AddOrUpdateEntityStateAsync()
        {
            //TODO Polly
            await this.StateManager.AddOrUpdateStateAsync(
                nameof(state),
                machine.State,
                (key, value) => machine.State
                );

            await this.StateManager.AddOrUpdateStateAsync(
               nameof(stateFlags),
               stateFlags,
               (key, value) => stateFlags
               );

            await StateManager.AddOrUpdateStateAsync(
                $"{nameof(Player)}",
                JsonConvert.SerializeObject(this._PosDevice),
                (key, value) => JsonConvert.SerializeObject(this._PosDevice));
        }

        private async Task RegisterKillMeReminder()
        {
            IActorReminder reminderRegistration = await this.RegisterReminderAsync(
                $"Kill.Me",
                null,
                TimeSpan.FromMinutes(15),   //The amount of time to delay before firing the reminder
                TimeSpan.FromMinutes(15)    //The time interval between firing of reminders
                );

            //TODO telemetry
        }

        private async Task RegisterProcessMeReminder()
        {
            IActorReminder reminderRegistration = await this.RegisterReminderAsync(
                $"Process.Me",
                null,
                TimeSpan.FromSeconds(5),   //The amount of time to delay before firing the reminder
                TimeSpan.FromSeconds(15)    //The time interval between firing of reminders
                );

            //TODO telemetry
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            ActorEventSource.Current.ActorMessage(this, $"{nameof(PosDeviceSimulationActor)} [TODO PosDeviceSimulationActor.Id] reminder recieved.");

            switch (reminderName)
            {
                case "Kill.Me":
                    if (
                        this.machine.IsInState(PosDeviceSimulationStateType.DeadManWalking)
                        || DateTimeOffset.UtcNow.AddMinutes(5) > this._PosDevice.CreatedOn.UtcDateTime)
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
                    break;
                case "Process.Me":

                    switch (this.machine.State)
                    {
                        case PosDeviceSimulationStateType.None:
                            break;
                        case PosDeviceSimulationStateType.Created:
                            break;
                        case PosDeviceSimulationStateType.Initialized:
                            break;
                        case PosDeviceSimulationStateType.Registering:
                            // here then retry posdeviceregister, we failed during call
                            await this.machine.FireAsync(PosDeviceSimulationTriggerType.Registering);

                            break;
                        case PosDeviceSimulationStateType.Registered:
                            // this means i'm waiting to start a Tx

                            //TODO timer or whatever then

                            await this.machine.FireAsync(PosDeviceSimulationTriggerType.TxStart);

                            break;
                        case PosDeviceSimulationStateType.TxStarted:

                            break;
                        case PosDeviceSimulationStateType.TxReleasing:
                            break;
                        case PosDeviceSimulationStateType.TxReleased:
                            break;
                        case PosDeviceSimulationStateType.TxProcessing:
                            break;
                        case PosDeviceSimulationStateType.TxEnded:
                            break;
                        case PosDeviceSimulationStateType.DeadManWalking:
                            break;
                        default:
                            break;
                    }

                    break;
                default:
                    break;
            }
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

        #region Create

        private Task<bool> CanCreate(CancellationToken cancellationToken)
        {
            Debug.Assert(state == machine.State);
            return Task.FromResult(machine.CanFire(PosDeviceSimulationTriggerType.Create));
        }

        private async Task OnCreated()
        {
            if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.Created))
            {
                // Only way to clean up the Actor Instance so do it first
                await RegisterKillMeReminder();

                this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.Created;

                Debug.Assert(state == machine.State);

                this._PosDevice = new PosDevice();

                await AddOrUpdateEntityStateAsync();
                await StateManager.SaveStateAsync();
            }
        }

        #endregion Create

        #region Initialize

        private Task<bool> CanInitialize(CancellationToken cancellationToken)
        {
            Debug.Assert(state == machine.State);
            return Task.FromResult(machine.CanFire(PosDeviceSimulationTriggerType.Initialize));
        }

        private async Task CatchAll()
        {
            await Task.Delay(1);
        }

        private async Task OtherToInitialized()
        {
            await Task.Delay(1);
        }

        private async Task CreateToInitialized()
        {
            await Task.Delay(1);
        }

        private async Task OnInitialized(string appApiLicenseCode)
        {
            this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.Initialized;

            // map the values

            // mcp - sneek the app license into the Player.Data just cause its convenient state
            this._PosDevice.DeviceDescription = appApiLicenseCode;

            await AddOrUpdateEntityStateAsync();

            ActorEventSource.Current.ActorMessage(this, $"Actor [{this.GetActorReference().ActorId.GetGuidId()}] initialized.");
        }

        #endregion Initialize

        #region Register

        private Task<bool> CanRegister(CancellationToken cancellationToken)
        {
            Debug.Assert(state == machine.State);
            return Task.FromResult(machine.CanFire(PosDeviceSimulationTriggerType.Registering));
        }

        private async Task OnRegistering()
        {
            if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.Registering))
            {
                this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.Registering;

                Debug.Assert(state == machine.State);

                // call register api's







                await AddOrUpdateEntityStateAsync();


            }
        }

        private async Task OnRegistered()
        {
            if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.Registered))
            {
                this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.Registered;

                Debug.Assert(state == machine.State);

                this._PosDevice.CreatedOn = DateTimeOffset.UtcNow;

                await AddOrUpdateEntityStateAsync();
            }
        }

        #endregion Cancel

        #region TxStart

        private Task<bool> CanTxStart(CancellationToken cancellationToken)
        {
            Debug.Assert(state == machine.State);
            return Task.FromResult(machine.CanFire(PosDeviceSimulationTriggerType.TxStart));
        }

        private async Task OnTxStart(string appApiLicenseCode)
        {
            if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.TxStarted))
            {
                this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.TxStarted;

                Debug.Assert(state == machine.State);
                
                // instantiate ConsumerActor and play a game






                await AddOrUpdateEntityStateAsync();

                //await this.machine.Activate(PosDeviceSimulationStateType.TxStarted;
            }
        }

        private async Task OnTxStarted()
        {
            if (!this.stateFlags.HasFlag(PosDeviceSimulationStateType.Registered))
            {
                this.stateFlags = this.stateFlags | PosDeviceSimulationStateType.Registered;

                Debug.Assert(state == machine.State);

                this._PosDevice.CreatedOn = DateTimeOffset.UtcNow;

                await AddOrUpdateEntityStateAsync();
            }
        }

        #endregion Cancel

        
        

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

            //httpreq.Headers.Add("Lazlo-SimulationLicenseCode", SimulationLicenseCode);
            //httpreq.Headers.Add("Lazlo-AuthorityLicenseCode", AuthorityLicenseCode);
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

                await consumerActor.InitializeAsync(statusResponse.Data.ConsumerLicenseCode, Id.GetGuidId()).ConfigureAwait(false);
            }

            else
            {
                if (age >= 18)
                {
                    throw new CorrelationException($"Player registration failed: {message.StatusCode} {responseJson}") { CorrelationRefId = req.CorrelationRefId };
                }

                WriteTimedDebug("Player not registered due to age restriction");
            }
        }

        private void WriteTimedDebug(string message)
        {
            Debug.WriteLine($"{DateTimeOffset.Now}: {message}");
        }

        //private async Task SetRetailer()
        //{
        //    Uri requestUri = GetFullUri($"api/v2/retailers/display/bylocation/100/{_Latitude}/{_Longitude}");

        //    //SmartRequest<string> req = new SmartRequest<string>
        //    //{
        //    //    CorrelationRefId = Guid.NewGuid(),
        //    //    CreatedOn = DateTimeOffset.UtcNow,
        //    //    Data = null,
        //    //    Latitude = _Latitude,
        //    //    Longitude = _Longitude,
        //    //    Uuid = Guid.NewGuid().ToString()
        //    //};

        //    Guid correlationRefId = Guid.NewGuid();

        //    HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Get, requestUri);

        //    httpreq.Headers.Add("Lazlo-SimulationLicenseCode", SimulationLicenseCode);
        //    httpreq.Headers.Add("Lazlo-AuthorityLicenseCode", AuthorityLicenseCode);
        //    httpreq.Headers.Add("Lazlo-CorrelationRefId", correlationRefId.ToString());

        //    //string json = JsonConvert.SerializeObject(req);

        //    //httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        //    HttpResponseMessage message = await _Client.SendAsync(httpreq).ConfigureAwait(false);

        //    if (message.IsSuccessStatusCode)
        //    {
        //        string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

        //        var response = JsonConvert.DeserializeObject<SmartResponse<List<RetailerDisplay<Beacon>>>>(responseJson);

        //        await StateManager.SetStateAsync(RetailerRefIdKey, response.Data.First().RetailerRefId).ConfigureAwait(false);

        //        Debug.WriteLine("Retailer Set");
        //    }

        //    else
        //    {
        //        string err = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

        //        throw new CorrelationException($"SetRetailer failed: {message.StatusCode} {err}") { CorrelationRefId = correlationRefId };
        //    }
        //}
    }
}
