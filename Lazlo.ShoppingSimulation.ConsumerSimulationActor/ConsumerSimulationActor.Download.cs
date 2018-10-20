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

namespace Lazlo.ShoppingSimulation.ConsumerSimulationActor
{
    //public partial class ConsumerSimulationActor
    //{
    //    private async Task DownloadNextTicket()
    //    {
    //        try
    //        {
    //            var pendingTickets = await StateManager.GetStateAsync<List<TicketStatusDisplay>>(PendingTicketsKey);

    //            TicketStatusDisplay ticket = pendingTickets.First();

    //            EntitySecret entitySecret = await RetrieveEntityMediaAsync(ticket);

    //            if(entitySecret != null)
    //            {
    //                var secrets = await StateManager.GetOrAddStateAsync(SecretsKey, new List<EntitySecret>());

    //                secrets.Add(entitySecret);

    //                await StateManager.SetStateAsync(SecretsKey, secrets);
    //            }

    //            pendingTickets.Remove(ticket);

    //            await StateManager.SetStateAsync(PendingTicketsKey, pendingTickets);

    //            if(pendingTickets.Count == 0)
    //            {
    //                Debugger.Break();
    //            }

    //            else
    //            {
    //                await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.DownloadTickets);
    //            }
    //        }

    //        catch (Exception ex)
    //        {
    //            WriteTimedDebug(ex);
    //            await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.DownloadTickets);
    //        }
    //    }

    //    private async Task<EntitySecret> RetrieveEntityMediaAsync(TicketStatusDisplay ticket)
    //    {
    //        WriteTimedDebug($"Begin retrieve ticket media. {ticket.TicketTemplateType} {ticket.TicketRefId} {ticket.MediaSize}\n{ticket.SasUri}");

    //        int chunkSize = ticket.MediaSize / 100;

    //        chunkSize = chunkSize > 5000 ? chunkSize : 5000;

    //        long read;

    //        string mediaType = null;

    //        using (HttpClient client = new HttpClient())
    //        using (MemoryStream ms = new MemoryStream())
    //        {
    //            long offset = 0;

    //            do
    //            {
    //                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, new Uri(ticket.SasUri));
    //                req.Headers.Range = new RangeHeaderValue(offset, offset + chunkSize - 1);

    //                HttpResponseMessage message = await client.SendAsync(req).ConfigureAwait(false);

    //                if (!message.IsSuccessStatusCode)
    //                {
    //                    // If we've reached forbidden the token has expired. If not found, we may have already downloaded it, but were demoted.
    //                    if (message.StatusCode == System.Net.HttpStatusCode.Forbidden
    //                       || message.StatusCode == System.Net.HttpStatusCode.NotFound)
    //                    {
    //                        return null;
    //                    }

    //                    throw new Exception("Error downloading media");
    //                }

    //                byte[] buffer = await message.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

    //                ms.Write(buffer, 0, buffer.Length);

    //                read = message.Content.Headers.ContentLength.Value;

    //                offset += read;

    //                mediaType = message.Content.Headers.ContentType.MediaType;
    //            }

    //            while (offset < ticket.MediaSize);

    //            return await ExtractTicketSecrets(ticket, mediaType, ms);
    //        }
    //    }

    //    private async Task<EntitySecret> ExtractTicketSecrets(TicketStatusDisplay ticketStatus, string mediaType, MemoryStream ticketStream)
    //    {
    //        string ticketLicenseCode = null;

    //        byte[] ticketBytes = ticketStream.ToArray();

    //        string encodedHash = CryptographyHelper.HashSha256(ticketBytes);

    //        if (mediaType == "video/mp4")
    //        {
    //            ticketStream.Position = 0;

    //            ticketLicenseCode = await TryParseMp4(ticketStream);

    //            if (ticketLicenseCode != null)
    //            {
    //                return new EntitySecret
    //                {
    //                    EntityRefId = ticketStatus.TicketRefId,
    //                    Hash = encodedHash,
    //                    LicenseCode = ticketLicenseCode
    //                };
    //            }
    //        }

    //        else
    //        {
    //            string qrCode = QrCodeHelper.ParseImage(ticketBytes);

    //            ticketLicenseCode = await ExtracTicketLicenseCodeViaTagsAsync(ticketStream).ConfigureAwait(false);

    //            if (ticketLicenseCode != qrCode)
    //            {
    //                WriteTimedDebug("Tag mismatch");
    //            }
    //        }

    //        if (ticketLicenseCode == null)
    //        {
    //            return null;
    //        }

    //        else
    //        {
    //            return new EntitySecret
    //            {
    //                EntityRefId = ticketStatus.TicketRefId,
    //                Hash = encodedHash,
    //                LicenseCode = ticketLicenseCode
    //            };
    //        }
    //    }

    //    private static async Task<string> TryParseMp4(Stream mp4Stream)
    //    {
    //        try
    //        {
    //            var tags = await Mp4TagExtractor.ParseTags(mp4Stream);

    //            var commentTag = tags.FirstOrDefault(z => z.Key == "�cmt");

    //            return commentTag.Key == "�cmt" ? commentTag.Value : null;
    //        }

    //        catch
    //        {
    //            return null;
    //        }
    //    }

    //    private Task<string> ExtracTicketLicenseCodeViaTagsAsync(Stream imageStream)
    //    {
    //        imageStream.Position = 0;

    //        MagickImage magickImage = new MagickImage(imageStream);

    //        ExifProfile exifProfile = magickImage.GetExifProfile();

    //        var target = exifProfile?.Values?.FirstOrDefault(z => z.Tag == ExifTag.ImageUniqueID);

    //        return Task.FromResult((string)target?.Value);
    //    }
    //}
}
