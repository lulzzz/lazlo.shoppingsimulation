using Lazlo.ShoppingSimulation.Common;
using Stateless;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lazlo.ShoppingSimulation.PosDeviceSimulationActor
{
    public partial class PosDeviceSimulationActor
    {
        private StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType> _Machine;

        private StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>.TriggerWithParameters<string, List<ApiLicenseDisplay>, PosDeviceModes> _InitializeTrigger;
        private StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>.TriggerWithParameters<string> _CheckoutPendingTrigger;

        private void ConfigureMachine()
        {
            _Machine = new StateMachine<PosDeviceSimulationStateType, PosDeviceSimulationTriggerType>(() => this.state, s => this.state = s);

            _InitializeTrigger = _Machine.SetTriggerParameters<string, List<ApiLicenseDisplay>, PosDeviceModes>(PosDeviceSimulationTriggerType.InitializeActor);
            _CheckoutPendingTrigger = _Machine.SetTriggerParameters<string>(PosDeviceSimulationTriggerType.QueueCheckoutPending);

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
                .Permit(PosDeviceSimulationTriggerType.WaitForConsumer, PosDeviceSimulationStateType.WaitingForConsumer)
                .Permit(PosDeviceSimulationTriggerType.GoIdle, PosDeviceSimulationStateType.Idle);          // An error occurred creating the consumer, go back to idle, try again next loop

            _Machine.Configure(PosDeviceSimulationStateType.WaitingForConsumer)
                .Permit(PosDeviceSimulationTriggerType.QueueCheckoutPending, PosDeviceSimulationStateType.CheckoutPendingQueued);

            _Machine.Configure(PosDeviceSimulationStateType.CheckoutPendingQueued)
                .OnEntryFromAsync(_CheckoutPendingTrigger, async (a) => await WaitingForConsumerToQueueCheckoutPending(a))
                .Permit(PosDeviceSimulationTriggerType.ProcessCheckoutPending, PosDeviceSimulationStateType.ProcessingCheckoutPending);

            _Machine.Configure(PosDeviceSimulationStateType.ProcessingCheckoutPending)
                .OnEntryAsync(async () => await CheckoutCompletePendingAsync());


            //_Machine.Configure(PosDeviceSimulationStateType.Registering)
            //    .SubstateOf(PosDeviceSimulationStateType.Initialized)
            //    .OnEntryAsync(async () => await OnRegistering())
            //    //.OnExitAsync(() => OnDeadManWalking())
            //    ;

            //_Machine.Configure(PosDeviceSimulationStateType.Registered)
            //    .SubstateOf(PosDeviceSimulationStateType.Initialized)
            //    .OnEntryAsync(async () => await OnRegistered())
            //    //.OnExitAsync(() => OnDeadManWalking())
            //    ;

            //txStartTrigger = _Machine.SetTriggerParameters<string>(PosDeviceSimulationTriggerType.TxStart);
            //_Machine.Configure(PosDeviceSimulationStateType.TxStarted)
            //    .Permit(PosDeviceSimulationTriggerType.TxRelease, PosDeviceSimulationStateType.TxReleased)
            //    .OnEntryFromAsync(txStartTrigger, (appApiLicenseCode) => OnTxStart(appApiLicenseCode))
            //    ;
        }
    }

    public enum PosDeviceSimulationTriggerType
    {
        // reserved
        CreateActor = 0,
        InitializeActor = 1,
        GoIdle = 2,
        GetNextInLine = 4,
        WaitForConsumer = 8,
        QueueCheckoutPending = 16,
        ProcessCheckoutPending = 32
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
        WaitingForConsumer = 16,
        CheckoutPendingQueued = 32,
        ProcessingCheckoutPending = 64,
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
