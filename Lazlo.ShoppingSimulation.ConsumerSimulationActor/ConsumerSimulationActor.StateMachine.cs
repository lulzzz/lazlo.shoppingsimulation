﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Lazlo.ShoppingSimulation.Common;
using Stateless;

namespace Lazlo.ShoppingSimulation.ConsumerSimulationActor
{
    public partial class ConsumerSimulationActor
    {
        private string StateKey = "StateKey";

		private StateMachine<ConsumerSimulationStateType, ConsumerSimulationWorkflowActions> _StateMachine;

        private StateMachine<ConsumerSimulationStateType, ConsumerSimulationWorkflowActions>.TriggerWithParameters<string, string> _InitializeTrigger;

        private StateMachine<ConsumerSimulationStateType, ConsumerSimulationWorkflowActions>.TriggerWithParameters<Guid, PosDeviceModes> _AssignPosTrigger;

        private void ConfigureStateMachine()
        {
            _StateMachine = new StateMachine<ConsumerSimulationStateType, ConsumerSimulationWorkflowActions>(
                () => StateManager.GetOrAddStateAsync(StateKey, ConsumerSimulationStateType.None).Result,
                async (state) => await StateManager.SetStateAsync(StateKey, state));

            _InitializeTrigger = _StateMachine.SetTriggerParameters<string, string>(ConsumerSimulationWorkflowActions.InitializeActor);
            _AssignPosTrigger = _StateMachine.SetTriggerParameters<Guid, PosDeviceModes>(ConsumerSimulationWorkflowActions.AssignPos);

            _StateMachine.OnTransitionedAsync(LogTransitionAsync);

            _StateMachine.Configure(ConsumerSimulationStateType.None)
                .Permit(ConsumerSimulationWorkflowActions.CreateActor, ConsumerSimulationStateType.ActorCreated);

            _StateMachine.Configure(ConsumerSimulationStateType.ActorCreated)
                .Permit(ConsumerSimulationWorkflowActions.InitializeActor, ConsumerSimulationStateType.ActorInitializing)
                .OnExitAsync(() =>
                {
                    WriteTimedDebug("Exiting Actor Created");
                    return Task.CompletedTask;
                });

            _StateMachine.Configure(ConsumerSimulationStateType.ActorInitializing)
                .OnEntryFromAsync(_InitializeTrigger, async (a, b) => await CreateToInitialized(a, b))
                .Permit(ConsumerSimulationWorkflowActions.RetrieveChannelGroups, ConsumerSimulationStateType.RetrievingChannelGroups);

            _StateMachine.Configure(ConsumerSimulationStateType.RetrievingChannelGroups)
                .OnEntryAsync(async () => await RetrieveChannelGroupAsync())
				.Permit(ConsumerSimulationWorkflowActions.GetInLine, ConsumerSimulationStateType.WaitingInLine);

            _StateMachine.Configure(ConsumerSimulationStateType.WaitingInLine)
                .Permit(ConsumerSimulationWorkflowActions.AssignPos, ConsumerSimulationStateType.PosAssigned);

            _StateMachine.Configure(ConsumerSimulationStateType.PosAssigned)
                .OnEntryFromAsync(_AssignPosTrigger, async (a, b) => await AssignPosAsync(a, b))
                .Permit(ConsumerSimulationWorkflowActions.ApproachPos, ConsumerSimulationStateType.WaitingToCheckout);

            _StateMachine.Configure(ConsumerSimulationStateType.WaitingToCheckout)
                .Permit(ConsumerSimulationWorkflowActions.Checkout, ConsumerSimulationStateType.CheckingOut);
            
            _StateMachine.Configure(ConsumerSimulationStateType.CheckingOut)
                .OnEntryAsync(async () => await CreateTicketCheckoutRequest())
				.Permit(ConsumerSimulationWorkflowActions.WaitForTicketsToRender, ConsumerSimulationStateType.WaitingForTicketsToRender)
                .Permit(ConsumerSimulationWorkflowActions.ApproachPos, ConsumerSimulationStateType.WaitingToCheckout);  //Checkout failed

            _StateMachine.Configure(ConsumerSimulationStateType.WaitingForTicketsToRender)
                .Permit(ConsumerSimulationWorkflowActions.CheckTicketStatus, ConsumerSimulationStateType.CheckingTicketStatus);

            _StateMachine.Configure(ConsumerSimulationStateType.CheckingTicketStatus)
                .OnEntryAsync(async () => await RetrieveCheckoutStatus())
                .Permit(ConsumerSimulationWorkflowActions.WaitForTicketsToRender, ConsumerSimulationStateType.WaitingForTicketsToRender);
        }

        private Task LogTransitionAsync(StateMachine<ConsumerSimulationStateType, ConsumerSimulationWorkflowActions>.Transition arg)
        {
            WriteTimedDebug($"Consumer transition from {arg.Source} to {arg.Destination}");

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
                WriteTimedDebug("Unable to save transition history");
            }
        }
    }

    public enum ConsumerSimulationWorkflowActions
    {
        CreateActor = 0,
        InitializeActor = 1,
        RetrieveChannelGroups = 2,
		GetInLine,
        AssignPos,
        ApproachPos,
		Checkout,
		WaitForTicketsToRender,
        CheckTicketStatus
    }

    [Flags]
    public enum ConsumerSimulationStateType
    {
        None = 0,
        ActorCreated = 1,
        ActorInitializing = 2,
		RetrievingChannelGroups = 4,
		WaitingInLine = 8,
        PosAssigned = 16,
        WaitingToCheckout = 32,
		CheckingOut = 64,
        WaitingForTicketsToRender = 128,
		CheckingTicketStatus = 256,
        DeadManWalking = 8196
    }
}