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
    internal class PosDeviceSimulationActor : Actor, IPosDeviceSimulationActor
    {
        const string InitializedKey = "InitializedKey";
        const string LicenseCodeKey = "LicenseCodeKey";
        const string PosDeviceModeKey = "PosDeviceModeKey";

        /// <summary>
        /// Initializes a new instance of PosDeviceSimulationActor
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public PosDeviceSimulationActor(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {
        }

        public async Task InitializeAsync(string licenseCode, PosDeviceModes posDeviceMode)
        {
            bool isInitialized = await StateManager.GetOrAddStateAsync<bool>(InitializedKey, false).ConfigureAwait(false);

            if (isInitialized)
            {
                return;
            }

            await StateManager.SetStateAsync(LicenseCodeKey, licenseCode).ConfigureAwait(false);
            await StateManager.SetStateAsync(PosDeviceModeKey, posDeviceMode).ConfigureAwait(false);
            await StateManager.SetStateAsync(InitializedKey, true).ConfigureAwait(false);
        }
    }
}
