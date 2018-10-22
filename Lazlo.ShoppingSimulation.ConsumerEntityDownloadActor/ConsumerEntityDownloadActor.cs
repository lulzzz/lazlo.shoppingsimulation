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
using Lazlo.Common.Requests;
using Newtonsoft.Json;
using Lazlo.Common;

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
        const bool _UseLocalHost = false;
        string _UriBase = "devshopapi.services.32point6.com";

        const string AppApiLicenseCodeKey = "AppApiLicenseCodeKey";
        const string CheckoutSessionLicenseCodeKey = "CheckoutSessionLicenseCodeKey";
        const string ConsumerRefIdKey = "ConsumerRefIdKey";
        const string ConsumerLicenseCodeKey = "ConsumerLicenseCodeKey";
        const string EntityDownloadKey = "EntityDownloadKey";
        const string ReminderKey = "ReminderKey";

        static readonly Uri ConsumerServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/ConsumerSimulationActorService");

        protected HttpClient _HttpClient = new HttpClient();

        /// <summary>
        /// Initializes a new instance of ConsumerEntityDownloadActor
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public ConsumerEntityDownloadActor(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {

        }

        public async Task InitalizeAsync(
            string appApiLicenseCodeKey,
            string checkoutSessionLicenseCode,
            Guid consumerRefId,
            string consumerLicenseCode,
            EntityDownload entityDownload)
        {
            if(!await StateManager.ContainsStateAsync(ConsumerRefIdKey))
            {
                await StateManager.SetStateAsync(AppApiLicenseCodeKey, appApiLicenseCodeKey);
                await StateManager.SetStateAsync(CheckoutSessionLicenseCodeKey, checkoutSessionLicenseCode);
                await StateManager.SetStateAsync(ConsumerLicenseCodeKey, consumerLicenseCode);
                await StateManager.SetStateAsync(ConsumerRefIdKey, consumerRefId);
                await StateManager.SetStateAsync(EntityDownloadKey, entityDownload);
            }

            await RegisterReminderAsync(ReminderKey, null, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5));
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                EntitySecret entitySecret = await RetrieveEntityMediaAsync().ConfigureAwait(false);

                WriteTimedDebug($"Ticket download complete: {entitySecret.ValidationLicenseCode}");

                await CallEntityReceivedAsync(entitySecret);

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
            EntityDownload entityDownload = await StateManager.GetStateAsync<EntityDownload>(EntityDownloadKey);

            WriteTimedDebug($"Begin retrieve {entityDownload.MediaEntityType} {entityDownload.MediaSize}\n{entityDownload.SasReadUri}");

            long chunkSize = entityDownload.MediaSize / 100;

            chunkSize = chunkSize > 5000 ? chunkSize : 5000;

            long read;

            string mediaType = null;

            using (HttpClient client = new HttpClient())
            using (MemoryStream ms = new MemoryStream())
            {
                long offset = 0;

                do
                {
                    HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, new Uri(entityDownload.SasReadUri));
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

                while (offset < entityDownload.MediaSize);

                return await ExtractTicketSecrets(entityDownload, mediaType, ms);
            }
        }

        private async Task<EntitySecret> ExtractTicketSecrets(EntityDownload entityDownload, string mediaType, MemoryStream ticketStream)
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
                        MediaEntityType = entityDownload.MediaEntityType,
                        Hash = encodedHash,
                        ValidationLicenseCode = entityDownload.ValidationLicenseCode
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
                    MediaEntityType = entityDownload.MediaEntityType,
                    Hash = encodedHash,
                    ValidationLicenseCode = entityDownload.ValidationLicenseCode
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

        private Uri GetFullUri(string fragment)
        {
            if (_UseLocalHost)
            {
                return new Uri($"http://localhost:8343/{fragment}");
            }

            else
            {
                return new Uri($"http://{_UriBase}/{fragment}");
            }
        }

        public async Task CallEntityReceivedAsync(EntitySecret entitySecret)
        {
            try
            {
                Uri requestUri = GetFullUri("api/v1/shopping/checkout/entity/received");

                HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

                string appApiLicenseCode = await StateManager.GetStateAsync<string>(AppApiLicenseCodeKey).ConfigureAwait(false);
                string consumerLicenseCode = await StateManager.GetStateAsync<string>(ConsumerLicenseCodeKey).ConfigureAwait(false);
                string checkoutSessionLicenseCode = await StateManager.GetStateAsync<string>(CheckoutSessionLicenseCodeKey).ConfigureAwait(false);

                httpreq.Headers.Add("lazlo-consumerlicensecode", consumerLicenseCode);
                httpreq.Headers.Add("lazlo-apilicensecode", appApiLicenseCode);
                httpreq.Headers.Add("lazlo-txlicensecode", checkoutSessionLicenseCode);

                SmartRequest<EntityReceivedRequest> entityReceivedRequest = new SmartRequest<EntityReceivedRequest>
                {
                    Data = new EntityReceivedRequest
                    {
                        EntityLicenseCode = entitySecret.ValidationLicenseCode,
                        MediaHash = entitySecret.Hash
                    }
                };

                string json = JsonConvert.SerializeObject(entityReceivedRequest);

                httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                HttpResponseMessage message = await _HttpClient.SendAsync(httpreq);

                if(message.IsSuccessStatusCode)
                {
                    WriteTimedDebug($"EntityReceived success: {entitySecret.ValidationLicenseCode}");
                }

                else
                {
                    string err = await message.Content.ReadAsStringAsync();

                    throw new Exception($"EntityReceived failed: {err}");
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                //Ignore for now
            }
        }
    }
}
