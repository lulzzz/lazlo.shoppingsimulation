﻿using Microsoft.ServiceFabric.Actors;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Lazlo.ShoppingSimulation.Common.Interfaces
{
    public interface IConsumerExchangeActor : IActor
    {
        Task InitializeAsync(string appApiLicenseCode, string consumerLicenseCode, List<EntitySecret> entities);

        Task UpdateDownloadStatusAsync(EntitySecret entitySecret);
    }
}
