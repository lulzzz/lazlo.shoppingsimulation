using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Stateless;

namespace Lazlo.ShoppingSimulation.ConsumerSimulationActor
{
    public partial class ConsumerSimulationActor
    {
        private string StateKey = "StateKey";

		private StateMachine<ConsumerSimulationStateType, ConsumerSimulationWorkflowActions> _StateMachine;

        private StateMachine<ConsumerSimulationStateType, ConsumerSimulationWorkflowActions>.TriggerWithParameters<string, string, Guid> _InitializeTrigger;

        private void ConfigureStateMachine()
        {
            _StateMachine = new StateMachine<ConsumerSimulationStateType, ConsumerSimulationWorkflowActions>(
                () => StateManager.GetOrAddStateAsync(StateKey, ConsumerSimulationStateType.None).Result,
                async (state) => await StateManager.SetStateAsync(StateKey, state));

            _InitializeTrigger = _StateMachine.SetTriggerParameters<string, string, Guid>(ConsumerSimulationWorkflowActions.InitializeActor);

            _StateMachine.OnTransitionedAsync(LogTransitionAsync);

            _StateMachine.Configure(ConsumerSimulationStateType.None)
                .Permit(ConsumerSimulationWorkflowActions.CreateActor, ConsumerSimulationStateType.ActorCreated);

            _StateMachine.Configure(ConsumerSimulationStateType.ActorCreated)
                .Permit(ConsumerSimulationWorkflowActions.InitializeActor, ConsumerSimulationStateType.ActorInitializing);

            _StateMachine.Configure(ConsumerSimulationStateType.ActorInitializing)
                .OnEntryFromAsync(_InitializeTrigger, async (a, b, c) => await CreateToInitialized(a, b, c))
                .Permit(ConsumerSimulationWorkflowActions.RetrieveChannelGroups, ConsumerSimulationStateType.RetrievingChannelGroups);

            _StateMachine.Configure(ConsumerSimulationStateType.RetrievingChannelGroups)
                .OnEntryAsync(async () => await RetrieveChannelGroupAsync())
				.Permit(ConsumerSimulationWorkflowActions.GoShopping, ConsumerSimulationStateType.ReadyToCheckout);

            _StateMachine.Configure(ConsumerSimulationStateType.ReadyToCheckout)
				.Permit(ConsumerSimulationWorkflowActions.Checkout, ConsumerSimulationStateType.CheckingOut);

            _StateMachine.Configure(ConsumerSimulationStateType.CheckingOut)
                .OnEntryAsync(async () => await CreateTicketCheckoutRequest())
				.Permit(ConsumerSimulationWorkflowActions.PollTickets, ConsumerSimulationStateType.PollingTickets);
        }

        private Task LogTransitionAsync(StateMachine<ConsumerSimulationStateType, ConsumerSimulationWorkflowActions>.Transition arg)
        {
            Debug.WriteLine($"Transition from {arg.Source} to {arg.Destination}");

            return Task.CompletedTask;

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
    }

    public enum ConsumerSimulationWorkflowActions
    {
        CreateActor = 0,
        InitializeActor = 1,
        RetrieveChannelGroups = 2,
		GoShopping,
		Checkout,
		PollTickets		
    }

    [Flags]
    public enum ConsumerSimulationStateType
    {
        None = 0,
        ActorCreated = 1,
        ActorInitializing = 2,
		RetrievingChannelGroups = 4,
		ReadyToCheckout = 8,
		CheckingOut = 16,
		PollingTickets = 32,
        DeadManWalking = 8196
    }
}
