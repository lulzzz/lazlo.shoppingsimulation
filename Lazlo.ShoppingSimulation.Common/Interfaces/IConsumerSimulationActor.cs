﻿using Microsoft.ServiceFabric.Actors;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lazlo.ShoppingSimulation.Common.Interfaces
{
    /// <summary>
    /// This interface defines the methods exposed by an actor.
    /// Clients use this interface to interact with the actor that implements it.
    /// </summary>
    public interface IConsumerSimulationActor : IActor
    {
        Task InitializeAsync(string appApiLicenseKey, string consumerLicenseCode, Guid posDeviceActorId, PosDeviceModes posDeviceMode);

        Task<string> RetrieveCheckoutLicenseCode();
    }
}
