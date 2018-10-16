using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace Lazlo.ShoppingSimulation.ConsumerSimulationActor
{
    public partial class ConsumerSimulationActor
    {
        //private async Task RetrieveTicketMediaAsync(List<CheckoutSummary> summaries, CheckoutSummary targetSummary, TicketStatusDisplay ticketStatus)
        //{
        //    WriteTimedDebug($"Begin retrieve ticket media. {ticketStatus.TicketTemplateType} {ticketStatus.TicketRefId} {ticketStatus.MediaSize}\n{ticketStatus.SasUri}");

        //    int chunkSize = ticketStatus.MediaSize / 100;

        //    chunkSize = chunkSize > 5000 ? chunkSize : 5000;

        //    long read;

        //    string mediaType = null;

        //    using (HttpClient client = new HttpClient())
        //    using (MemoryStream ms = new MemoryStream())
        //    {
        //        long offset = 0;

        //        do
        //        {
        //            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, new Uri(ticketStatus.SasUri));
        //            req.Headers.Range = new RangeHeaderValue(offset, offset + chunkSize - 1);

        //            HttpResponseMessage message = await client.SendAsync(req).ConfigureAwait(false);

        //            if (!message.IsSuccessStatusCode)
        //            {
        //                // If we've reached forbidden the token has expired. If not found, we may have already downloaded it, but were demoted.
        //                if (message.StatusCode == System.Net.HttpStatusCode.Forbidden
        //                   || message.StatusCode == System.Net.HttpStatusCode.NotFound)
        //                {
        //                    targetSummary.Tickets.Remove(ticketStatus);

        //                    if (targetSummary.Tickets.Count == 0 && targetSummary.TicketSecrets.Count == 0)
        //                    {
        //                        summaries.Remove(targetSummary);
        //                    }

        //                    await StateManager.SetStateAsync(TicketSummariesKey, summaries).ConfigureAwait(false);
        //                }

        //                throw new SecurityException($"Error occurred while downloading ticket image {message.StatusCode} {offset} - {offset + chunkSize - 1}");
        //            }

        //            byte[] buffer = await message.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        //            ms.Write(buffer, 0, buffer.Length);

        //            read = message.Content.Headers.ContentLength.Value;

        //            offset += read;

        //            mediaType = message.Content.Headers.ContentType.MediaType;
        //        }

        //        while (offset < ticketStatus.MediaSize);

        //        TicketSecrets ticketSecret = await ExtractTicketSecrets(targetSummary, ticketStatus, mediaType, ms);

        //        if (ticketSecret != null)
        //        {
        //            await SendTicketReceived(ticketSecret).ConfigureAwait(false);

        //            targetSummary.TicketSecrets.Add(ticketSecret);
        //        }

        //        targetSummary.Tickets.Remove(ticketStatus);

        //        if (targetSummary.Tickets.Count == 0 && targetSummary.TicketSecrets.Count == 0)
        //        {
        //            summaries.Remove(targetSummary);
        //        }

        //        await StateManager.SetStateAsync(TicketSummariesKey, summaries).ConfigureAwait(false);

        //        WriteTimedDebug($"Removed status. {ticketStatus.TicketRefId}");

        //        int ticketsReceived = await StateManager.GetStateAsync<int>(TicketsReceivedKey).ConfigureAwait(false);

        //        await StateManager.SetStateAsync(TicketsReceivedKey, ticketsReceived + 1).ConfigureAwait(false);
        //    }
        //}
    }
}
