using Lazlo.Common;
using Lazlo.Common.Models;
using Microsoft.ServiceFabric.Actors;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Lazlo.ShoppingSimulation.Common.Interfaces
{
    public interface IConsumerEntityDownloadActor : IActor
    {
        Task InitalizeAsync(
            string appApiLicenseCodeKey,
            string checkoutSessionLicenseCode,
            Uri serviceUri,
            Guid sourceActorId,
            string consumerLicenseCode,
            EntityDownload entityDownload);
    }
}
