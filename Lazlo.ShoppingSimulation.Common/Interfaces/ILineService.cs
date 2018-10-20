using Microsoft.ServiceFabric.Services.Remoting;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Lazlo.ShoppingSimulation.Common.Interfaces
{
    public interface ILineService : IService
    {
        Task GetInLineAsync(string appApiLicenseCode, Guid consumerActorId);

        Task<Guid> GetNextConsumerInLineAsync(string appApiLicenseCode);
    }
}
