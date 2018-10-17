using Microsoft.ServiceFabric.Actors;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Lazlo.ShoppingSimulation.Common.Interfaces
{
    public interface IConsumerExchangeActor : IActor
    {
        Task InitializeAsync(string consumerLicenseCode, List<EntitySecret> entities);
    }
}
