using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lazlo.ShoppingSimulation.Common;
using Stateless;

namespace Lazlo.ShoppingSimulation.ConsumerExchangeActor
{
    public partial class ConsumerExchangeActor
    {
        private string StateKey = "StateKey";

        private StateMachine<ConsumerSimulationExchangeState, ConsumerSimulationExchangeAction> _StateMachine;

        private StateMachine<ConsumerSimulationExchangeState, ConsumerSimulationExchangeAction>.TriggerWithParameters<string, string, List<EntitySecret>> _InitializeTrigger;

        private void ConfigureStateMachine()
        {
            _StateMachine = new StateMachine<ConsumerSimulationExchangeState, ConsumerSimulationExchangeAction>(
                () => StateManager.GetOrAddStateAsync(StateKey, ConsumerSimulationExchangeState.None).Result,
                async (state) => await StateManager.SetStateAsync(StateKey, state));

            _InitializeTrigger = _StateMachine.SetTriggerParameters<string, string, List<EntitySecret>>(ConsumerSimulationExchangeAction.InitializeActor);

            _StateMachine.Configure(ConsumerSimulationExchangeState.None)
                .Permit(ConsumerSimulationExchangeAction.CreateActor, ConsumerSimulationExchangeState.ActorCreated);

            _StateMachine.Configure(ConsumerSimulationExchangeState.ActorCreated)
                .Permit(ConsumerSimulationExchangeAction.InitializeActor, ConsumerSimulationExchangeState.ActorInitializing);

            _StateMachine.Configure(ConsumerSimulationExchangeState.ActorInitializing)
                .OnEntryFromAsync(_InitializeTrigger, async (a, b, c) => await OnInitialized(a, b, c))
                .Permit(ConsumerSimulationExchangeAction.GoIdle, ConsumerSimulationExchangeState.Idle);

            _StateMachine.Configure(ConsumerSimulationExchangeState.Idle)
                .Permit(ConsumerSimulationExchangeAction.Validate, ConsumerSimulationExchangeState.Validating);

            _StateMachine.Configure(ConsumerSimulationExchangeState.Validating)
                .OnEntryAsync(async () => await OnValidateAsync())
                .Permit(ConsumerSimulationExchangeAction.GoIdle, ConsumerSimulationExchangeState.Idle);
        }

        private async Task ProcessWorkflowAsync()
        {
            switch (_StateMachine.State)
            {
                case ConsumerSimulationExchangeState.Idle:
                    await _StateMachine.FireAsync(ConsumerSimulationExchangeAction.Validate);
                    break;
            }
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            switch (reminderName)
            {
                case WorkflowReminderKey:
                    await ProcessWorkflowAsync();
                    break;
            }
        }
    }

    public enum ConsumerSimulationExchangeState
    {
        None,
        ActorCreated,
        ActorInitializing,
        Idle,
        Validating
    }

    public enum ConsumerSimulationExchangeAction
    {
        CreateActor,
        InitializeActor,
        GoIdle,
        Validate
    }
}
