using Lazlo.ShoppingSimulation.Common;
using Stateless;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Lazlo.ShoppingSimulation.PosDeviceSimulationActor
{
    public partial class PosDeviceSimulationActor
    {
        private StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType> _Machine;

        private StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>.TriggerWithParameters<string, List<ApiLicenseDisplay>, PosDeviceModes> _InitializeTrigger;

        private void ConfigureMachine()
        {
            _Machine = new StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>(() => this.state, s => this.state = s);

            _InitializeTrigger = _Machine.SetTriggerParameters<string, List<ApiLicenseDisplay>, PosDeviceModes>(PosDeviceSimulationTriggerType.InitializeActor);

            _Machine.OnTransitionedAsync(LogTransitionAsync);

            _Machine.Configure(PosDeviceSimulationStateType.None)
                .Permit(PosDeviceSimulationTriggerType.CreateActor, PosDeviceSimulationStateType.ActorCreated);

            _Machine.Configure(PosDeviceSimulationStateType.ActorCreated)
                .OnEntryAsync(async () => await OnCreated())
                .Permit(PosDeviceSimulationTriggerType.InitializeActor, PosDeviceSimulationStateType.ActorInitializing);

            _Machine.Configure(PosDeviceSimulationStateType.ActorInitializing)
                .OnEntryFromAsync(_InitializeTrigger, async (a, b, c) => await CreateToInitialized(a, b, c))
                .Permit(PosDeviceSimulationTriggerType.GoIdle, PosDeviceSimulationStateType.Idle);

            _Machine.Configure(PosDeviceSimulationStateType.Idle)
                //.OnEntryFromAsync(_InitializeTrigger, async (a, b, c) => await CreateToInitialized(a, b, c))
                .Permit(PosDeviceSimulationTriggerType.GetNextInLine, PosDeviceSimulationStateType.NextInLine);

            _Machine.Configure(PosDeviceSimulationStateType.NextInLine)
                .OnEntry(async () => await GetNextInLine())
                .Permit(PosDeviceSimulationTriggerType.GoIdle, PosDeviceSimulationStateType.Idle)         // An error occurred creating the consumer, go back to idle, try again next loop
                .Permit(PosDeviceSimulationTriggerType.WaitForConsumerToCheckout, PosDeviceSimulationStateType.WaitingForConsumerToCheckout)
                .Permit(PosDeviceSimulationTriggerType.WaitForConsumerToPresentQr, PosDeviceSimulationStateType.WaitingForConsumerToPresentQr);

            _Machine.Configure(PosDeviceSimulationStateType.WaitingForConsumerToCheckout)
                .Permit(PosDeviceSimulationTriggerType.CallCheckoutCompletePending, PosDeviceSimulationStateType.CallingCheckoutCompletePending);

            _Machine.Configure(PosDeviceSimulationStateType.WaitingForConsumerToPresentQr)
                .Permit(PosDeviceSimulationTriggerType.ScanConsumerQr, PosDeviceSimulationStateType.ScanningConsumerQr);

            _Machine.Configure(PosDeviceSimulationStateType.ScanningConsumerQr)
                .OnEntry(async () => await ScanConsumerQr())
                .Permit(PosDeviceSimulationTriggerType.WaitForConsumerToPresentQr, PosDeviceSimulationStateType.WaitingForConsumerToPresentQr) // Consumer hasn't started session, loop back
                .Permit(PosDeviceSimulationTriggerType.WaitForConsumerToCheckout, PosDeviceSimulationStateType.WaitingForConsumerToCheckout);

            _Machine.Configure(PosDeviceSimulationStateType.CallingCheckoutCompletePending)
                .OnEntryAsync(async () => await GetLineItemsAsync())
                .Permit(PosDeviceSimulationTriggerType.WaitForConsumerToCheckout, PosDeviceSimulationStateType.WaitingForConsumerToCheckout)
                .Permit(PosDeviceSimulationTriggerType.WaitForConsumerToPay, PosDeviceSimulationStateType.WaitingForConsumerToPay);

            _Machine.Configure(PosDeviceSimulationStateType.WaitingForConsumerToPay)
                .Permit(PosDeviceSimulationTriggerType.AcceptPayment, PosDeviceSimulationStateType.CallingCheckoutComplete);

            _Machine.Configure(PosDeviceSimulationStateType.CallingCheckoutComplete)
                .OnEntryAsync(async () => await CallCheckoutCompleteAsync())
                .Permit(PosDeviceSimulationTriggerType.WaitForConsumerToPay, PosDeviceSimulationStateType.WaitingForConsumerToPay) //Loopback
                .Permit(PosDeviceSimulationTriggerType.GoIdle, PosDeviceSimulationStateType.Idle);
        }

        private async Task LogTransitionAsync(StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>.Transition arg)
        {
            WriteTimedDebug($"Pos transition from {arg.Source} to {arg.Destination}");

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

        public async Task ProcessWorkflow()
        {
            WriteTimedDebug($"PosDeviceSimulation ProcessWorkflowEntered: {_Machine.State}");

            switch (_Machine.State)
            {
                case PosDeviceSimulationStateType.Idle:
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.GetNextInLine);
                    break;

                case PosDeviceSimulationStateType.WaitingForConsumerToCheckout:
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.CallCheckoutCompletePending);
                    break;

                case PosDeviceSimulationStateType.WaitingForConsumerToPresentQr:
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.ScanConsumerQr);
                    break;

                case PosDeviceSimulationStateType.WaitingForConsumerToPay:
                    await _Machine.FireAsync(PosDeviceSimulationTriggerType.AcceptPayment);
                    break;
            }
        }
    }

    public enum PosDeviceSimulationTriggerType
    {
        // reserved
        CreateActor = 0,
        InitializeActor = 1,
        GoIdle = 2,
        GetNextInLine = 4,
        CallCheckoutCompletePending = 64,
        WaitForConsumerToCheckout = 128,
        WaitForConsumerToPresentQr = 256,
        WaitForConsumerToPay = 512,
        ScanConsumerQr = 1024,
        AcceptPayment = 2048,
        //CallCheckoutCompletePending = 8,
        //QueueCheckoutPending = 16
        //Registering = 16,
        //Registered = 32,
        //TxStart = 64,
        //TxRelease = 128, // idempotent
        //TxProcess = 512, // hand to ConsumerActor
        //TxEnded = 1024, // Actor reports back I'm done
    }

    [Flags]
    public enum PosDeviceSimulationStateType
    {
        None = 0,
        ActorCreated = 1,
        ActorInitializing = 2,
        Idle = 4,
        NextInLine = 8,
        CallingCheckoutCompletePending = 16,
        WaitingForConsumerToCheckout = 32,
        WaitingForConsumerToPresentQr = 64,
        ScanningConsumerQr = 128,
        WaitingForConsumerToPay = 256,
        CallingCheckoutComplete = 512,
        //WaitingForConsumer = 16,
        //CheckoutPendingQueued = 32,
        //ProcessingCheckoutPending = 64,
        //Registering = 16,
        //Registered = 32,
        //TxStarted = 64,
        //TxReleasing = 128, // idempotent
        //TxReleased = 256, // succeeeded in calling checkoutcomplete
        //TxProcessing = 512, // hand to ConsumerActor
        //TxEnded = 1024, // Actor reports back I'm done
        DeadManWalking = 8196
    }
}
