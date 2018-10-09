using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Actors.Client;
using Lazlo.ShoppingSimulation.Common.Interfaces;
using Stateless;
using Lazlo.Common.Enumerators;
using Lazlo.Common;
using Newtonsoft.Json;
using System.Diagnostics;

namespace Lazlo.ShoppingSimulation.TicketSimulationActor
{
    public enum TicketSimulationTriggerType
    {
        Create = 0,
        Initialize = 1,
        Void = 2,
        Render = 3,
        Recieve = 4,
        Kill = 100
    }

    [Flags]
    public enum TicketSimulationStateType
    {
        None = 0,
        Created = 1,
        Initialized = 2,
        Voided = 16,
        Processing = 32,
        Processed = 64,
        Rendering = 128,
        Rendered = 256,
        Recieving = 512,
        Recieved = 1024,
        Completed = Processed | Rendered | Recieved,
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
    [ActorService(Name = "TicketSimulationActor")]
    internal class TicketSimulationActor : Actor, ITicketSimulationActor
    {
        private TicketSimulationStateType state;
        private TicketSimulationStateType stateFlags;
        private StateMachine<TicketSimulationStateType, TicketSimulationTriggerType> machine;
        private StateMachine<TicketSimulationStateType, TicketSimulationTriggerType>.TriggerWithParameters<Guid, Lazlo.Common.Enumerators.SimulationType> initializedTrigger;

        private TicketStatus _TicketStatus { get; set; }

        /// <summary>
        /// Initializes a new instance of TicketSimulationActor
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public TicketSimulationActor(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {
            // This is a critical step
            // We have no way to clean up Actors without this call
            // Enumerating the services Actors is no realistic as it will have millions in production
            if (!this.stateFlags.HasFlag(TicketSimulationStateType.Created))
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
                var savedState = await this.StateManager.TryGetStateAsync<TicketSimulationStateType>($"{nameof(this.state)}");
                var savedStateFlags = await this.StateManager.TryGetStateAsync<TicketSimulationStateType>($"{nameof(this.stateFlags)}");

                ConfigureMachine();
                
                if (this.machine.IsInState(TicketSimulationStateType.None))
                {
                    // first load, initalize                    
                    await this.machine.FireAsync(TicketSimulationTriggerType.Create);
                }

                //TODO wrap in Polly                
                var tryGetEntity = StateManager.TryGetStateAsync<string>($"{nameof(TicketStatus)}").Result;
                if (tryGetEntity.HasValue) this._TicketStatus = JsonConvert.DeserializeObject<TicketStatus>(tryGetEntity.Value);

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
        public async Task<Guid> TicketInitialize(
            Guid checkoutSessionRefId,
            SimulationType simulationType
            )
        {
            if (this.machine.IsInState(TicketSimulationStateType.Created))
            {
                await this.machine.FireAsync(initializedTrigger, checkoutSessionRefId, simulationType);

                return this.GetActorReference().ActorId.GetGuidId();
            }

            ActorEventSource.Current.ActorMessage(this, $"{nameof(TicketSimulationActor)} already initialized.");
            throw new InvalidOperationException($"{nameof(TicketSimulationActor)} [{this.GetActorReference().ActorId.GetGuidId()}] has already been initialized.");
        }

        private void ConfigureMachine()
        {
            machine = new StateMachine<TicketSimulationStateType, TicketSimulationTriggerType>(
                () => this.state,
                s => this.state = s
                );

            // this state machine code is modified from https://github.com/dotnet-state-machine/stateless/tree/dev/example/BugTrackerExample
            // under the Apache 2.0 license: https://github.com/dotnet-state-machine/stateless/blob/dev/LICENSE
            machine.OnTransitionedAsync(LogTransitionAsync<TicketSimulationStateType, TicketSimulationTriggerType>);

            machine.Configure(TicketSimulationStateType.None)
                .Permit(TicketSimulationTriggerType.Create, TicketSimulationStateType.Created)
                ;

            machine.Configure(TicketSimulationStateType.Created)
                .Permit(TicketSimulationTriggerType.Initialize, TicketSimulationStateType.Initialized)
                .OnEntryAsync(async () => await OnCreated())
                ;

            initializedTrigger = machine.SetTriggerParameters<Guid, SimulationType>(TicketSimulationTriggerType.Initialize);
            machine.Configure(TicketSimulationStateType.Initialized)
                .Permit(TicketSimulationTriggerType.Void, TicketSimulationStateType.Voided)
                .Permit(TicketSimulationTriggerType.Render, TicketSimulationStateType.Rendered)
                .Permit(TicketSimulationTriggerType.Recieve, TicketSimulationStateType.Recieved)
                .OnEntryFromAsync(initializedTrigger, (checkoutSessionRefId, simulationType) => OnInitialized(checkoutSessionRefId, simulationType))
                ;

            machine.Configure(TicketSimulationStateType.Voided)
                .SubstateOf(TicketSimulationStateType.Initialized)
                .Permit(TicketSimulationTriggerType.Kill, TicketSimulationStateType.DeadManWalking)
                .OnEntryAsync(async () => await OnVoided())
                //.OnExitAsync(() => OnDeadManWalking())
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
                $"{nameof(CheckoutStatus)}",
                JsonConvert.SerializeObject(this._TicketStatus),
                (key, value) => JsonConvert.SerializeObject(this._TicketStatus));
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
            ActorEventSource.Current.ActorMessage(this, $"{nameof(TicketSimulationActor)} [TODO TicketSimulationActor.Id] reminder recieved.");

            switch (reminderName)
            {
                case "Kill.Me":
                    if (
                        this.machine.IsInState(TicketSimulationStateType.DeadManWalking)
                        || DateTimeOffset.UtcNow.AddMinutes(5) > this._TicketStatus.CreatedOn.UtcDateTime)
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
                        case TicketSimulationStateType.None:
                            break;
                        case TicketSimulationStateType.Created:
                            break;
                        case TicketSimulationStateType.Initialized:
                            break;
                        case TicketSimulationStateType.Voided:
                            break;                        
                        case TicketSimulationStateType.Rendering:
                            // call status update

                            // if GeneratedOn NOT null move to recieving
                            await this.machine.FireAsync(TicketSimulationTriggerType.Recieve);

                            break;
                        case TicketSimulationStateType.Rendered:
                            break;
                        case TicketSimulationStateType.Recieving:
                            
                            // call recieved

                            // if success move to recieved
                            await this.machine.FireAsync(TicketSimulationTriggerType.Kill);

                            break;
                        case TicketSimulationStateType.Recieved:
                            break;
                        case TicketSimulationStateType.Completed:
                            break;
                        case TicketSimulationStateType.DeadManWalking:
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
            return Task.FromResult(machine.CanFire(TicketSimulationTriggerType.Create));
        }

        private async Task OnCreated()
        {
            if (!this.stateFlags.HasFlag(TicketSimulationStateType.Created))
            {
                // Only way to clean up the Actor Instance so do it first
                await RegisterKillMeReminder();

                this.stateFlags = this.stateFlags | TicketSimulationStateType.Created;

                Debug.Assert(state == machine.State);

                this._TicketStatus = new TicketStatus() { TicketRefId = this.GetActorReference().ActorId.GetGuidId() };

                await AddOrUpdateEntityStateAsync();
                await StateManager.SaveStateAsync();
            }
        }

        #endregion Create

        #region Initialize

        private Task<bool> CanInitialize(CancellationToken cancellationToken)
        {
            Debug.Assert(state == machine.State);
            return Task.FromResult(machine.CanFire(TicketSimulationTriggerType.Initialize));
        }

        private async Task OnInitialized(Guid checkoutSessionRefId, SimulationType simulationType)
        {
            this.stateFlags = this.stateFlags | TicketSimulationStateType.Initialized;

            // map the values
            this._TicketStatus.CheckoutSessionRefId = checkoutSessionRefId;
            //this._TicketStatus.SimulationType = simulationType;

            await AddOrUpdateEntityStateAsync();
            
            ActorEventSource.Current.ActorMessage(this, $"Actor [{this.GetActorReference().ActorId.GetGuidId()}] initialized.");
        }

        #endregion Initialize

        #region Cancel

        private Task<bool> CanVoid(CancellationToken cancellationToken)
        {
            Debug.Assert(state == machine.State);
            return Task.FromResult(machine.CanFire(TicketSimulationTriggerType.Void));
        }

        private async Task OnVoided()
        {
            if (!this.stateFlags.HasFlag(TicketSimulationStateType.Voided))
            {
                this.stateFlags = this.stateFlags | TicketSimulationStateType.Voided;

                Debug.Assert(state == machine.State);

                this._TicketStatus.VoidedOn = DateTimeOffset.UtcNow;

                await AddOrUpdateEntityStateAsync();
            }
        }

        #endregion Cancel
    }
}
