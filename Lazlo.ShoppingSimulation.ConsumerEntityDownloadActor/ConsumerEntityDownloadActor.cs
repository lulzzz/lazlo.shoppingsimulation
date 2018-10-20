using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Actors.Client;
using Lazlo.ShoppingSimulation.Common.Interfaces;
using Lazlo.Common.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using Lazlo.ShoppingSimulation.Common;
using Lazlo.Utility;
using ImageMagick;
using System.Diagnostics;

namespace Lazlo.ShoppingSimulation.ConsumerEntityDownloadActor
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
    internal class ConsumerEntityDownloadActor : Actor, IConsumerEntityDownloadActor, IRemindable
    {
        const string ConsumerRefIdKey = "ConsumerRefIdKey";
        const string ConsumerLicenseCodeKey = "ConsumerLicenseCodeKey";
        const string TicketStatusDisplayKey = "TicketStatusDisplayKey";
        const string ReminderKey = "ReminderKey";

        static readonly Uri ConsumerServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/ConsumerSimulationActorService");

        /// <summary>
        /// Initializes a new instance of ConsumerEntityDownloadActor
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public ConsumerEntityDownloadActor(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {

        }

        public async Task InitalizeAsync(Guid consumerRefId, string consumerLicenseCode, TicketStatusDisplay ticketStatusDisplay)
        {
            if(!await StateManager.ContainsStateAsync(ConsumerRefIdKey))
            {
                await StateManager.SetStateAsync(ConsumerRefIdKey, consumerRefId);
                await StateManager.SetStateAsync(ConsumerLicenseCodeKey, consumerRefId);
                await StateManager.SetStateAsync(TicketStatusDisplayKey, ticketStatusDisplay);
            }

            await RegisterReminderAsync(ReminderKey, null, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5));
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                EntitySecret entitySecret = await RetrieveEntityMediaAsync().ConfigureAwait(false);

                WriteTimedDebug($"Ticket download complete: {entitySecret.ValidationLicenseCode}");

                Guid consumerId = await StateManager.GetStateAsync<Guid>(ConsumerRefIdKey);

                ActorId consumerActorId = new ActorId(consumerId);

                IConsumerSimulationActor consumerActor = ActorProxy.Create<IConsumerSimulationActor>(consumerActorId, ConsumerServiceUri);

                await consumerActor.UpdateDownloadStatusAsync(entitySecret);

                var reminder = GetReminder(ReminderKey);

                await UnregisterReminderAsync(reminder).ConfigureAwait(false);
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
            }
        }

        private async Task<EntitySecret> RetrieveEntityMediaAsync()
        {
            TicketStatusDisplay ticket = await StateManager.GetStateAsync<TicketStatusDisplay>(TicketStatusDisplayKey);

            WriteTimedDebug($"Begin retrieve ticket media. {ticket.TicketTemplateType} {ticket.MediaSize}\n{ticket.SasUri}");

            long chunkSize = ticket.MediaSize / 100;

            chunkSize = chunkSize > 5000 ? chunkSize : 5000;

            long read;

            string mediaType = null;

            using (HttpClient client = new HttpClient())
            using (MemoryStream ms = new MemoryStream())
            {
                long offset = 0;

                do
                {
                    HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, new Uri(ticket.SasUri));
                    req.Headers.Range = new RangeHeaderValue(offset, offset + chunkSize - 1);

                    HttpResponseMessage message = await client.SendAsync(req).ConfigureAwait(false);

                    if (!message.IsSuccessStatusCode)
                    {
                        // If we've reached forbidden the token has expired. If not found, we may have already downloaded it, but were demoted.
                        if (message.StatusCode == System.Net.HttpStatusCode.Forbidden
                           || message.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            return null;
                        }

                        throw new Exception("Error downloading media");
                    }

                    byte[] buffer = await message.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                    ms.Write(buffer, 0, buffer.Length);

                    read = message.Content.Headers.ContentLength.Value;

                    offset += read;

                    mediaType = message.Content.Headers.ContentType.MediaType;
                }

                while (offset < ticket.MediaSize);

                return await ExtractTicketSecrets(ticket, mediaType, ms);
            }
        }

        private async Task<EntitySecret> ExtractTicketSecrets(TicketStatusDisplay ticketStatus, string mediaType, MemoryStream ticketStream)
        {
            string ticketLicenseCode = null;

            byte[] ticketBytes = ticketStream.ToArray();

            string encodedHash = CryptographyHelper.HashSha256(ticketBytes);

            if (mediaType == "video/mp4")
            {
                ticketStream.Position = 0;

                ticketLicenseCode = await TryParseMp4(ticketStream);

                if (ticketLicenseCode != null)
                {
                    return new EntitySecret
                    {
                        Hash = encodedHash,
                        ValidationLicenseCode = ticketStatus.ValidationLicenseCode
                    };
                }
            }

            else
            {
                string qrCode = QrCodeHelper.ParseImage(ticketBytes);

                ticketLicenseCode = await ExtracTicketLicenseCodeViaTagsAsync(ticketStream).ConfigureAwait(false);

                if (ticketLicenseCode != qrCode)
                {
                    WriteTimedDebug("Tag mismatch");
                }
            }

            if (ticketLicenseCode == null)
            {
                return null;
            }

            else
            {
                return new EntitySecret
                {
                    Hash = encodedHash,
                    ValidationLicenseCode = ticketStatus.ValidationLicenseCode
                };
            }
        }

        private static async Task<string> TryParseMp4(Stream mp4Stream)
        {
            try
            {
                var tags = await Mp4TagExtractor.ParseTags(mp4Stream);

                var commentTag = tags.FirstOrDefault(z => z.Key == "�cmt");

                return commentTag.Key == "�cmt" ? commentTag.Value : null;
            }

            catch
            {
                return null;
            }
        }

        private Task<string> ExtracTicketLicenseCodeViaTagsAsync(Stream imageStream)
        {
            imageStream.Position = 0;

            MagickImage magickImage = new MagickImage(imageStream);

            ExifProfile exifProfile = magickImage.GetExifProfile();

            var target = exifProfile?.Values?.FirstOrDefault(z => z.Tag == ExifTag.ImageUniqueID);

            return Task.FromResult((string)target?.Value);
        }

        private void WriteTimedDebug(string message)
        {
            Debug.WriteLine($"{DateTimeOffset.Now}: {message}");
        }

        private void WriteTimedDebug(Exception ex)
        {
            Debug.WriteLine($"{DateTimeOffset.Now}: {ex}");
        }
    }
}
