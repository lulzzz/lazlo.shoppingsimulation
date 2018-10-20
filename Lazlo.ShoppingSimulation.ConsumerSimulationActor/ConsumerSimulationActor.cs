using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Actors.Client;
using Lazlo.ShoppingSimulation.Common.Interfaces;
using System.Diagnostics;
using Lazlo.Common.Enumerators;
using Lazlo.Common.Requests;
using Lazlo.Common;
using Lazlo.Gaming.Random;
using Lazlo.ShoppingSimulation.Common;
using System.Net.Http;
using Newtonsoft.Json;
using Lazlo.Common.Responses;
using Lazlo.Common.Models;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Services.Client;
using Microsoft.ServiceFabric.Services.Remoting.Client;

namespace Lazlo.ShoppingSimulation.ConsumerSimulationActor
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
    public partial class ConsumerSimulationActor : Actor, IConsumerSimulationActor, IRemindable
    {
        const string PosDeviceActorIdKey = "PosDeviceActorIdKey";
        const string ConsumerLicenseCodeKey = "ConsumerLicenseCodeKey";
        const string AppApiLicenseCodeKey = "AppApiLicenseCodeKey";
        const string ActionLicenseCodeKey = "ActionLicenseCodeKey";
        const string PosDeviceModeKey = "PosDeviceModeKey";
        const string ChannelGroupsKey = "ChannelGroupsKey";
        const string KillMeReminderKey = "KillMeReminderKey";
        const string WorkflowReminderKey = "WorkflowReminderKey";
        const string InProgressDownloadsKey = "InProgressDownloadsKey";
        const string TotalPurchasesKey = "TotalPurchasesKey";

        const bool _UseLocalHost = false;
        string _UriBase = "devshopapi.services.32point6.com";

        protected HttpClient _HttpClient = new HttpClient();

        static readonly Uri PosDeviceServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/PosDeviceSimulationActorService");
        static readonly Uri EntityDownloadServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/ConsumerEntityDownloadActorService");
        static readonly Uri LineServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/Lazlo.ShoppingSimulation.ConsumerLineService");

        //readonly string AuthorityLicenseCode = "0rsVpol+brlnpe@m@?K1y?WA$Cw?v*hbE[hv(5u*A5zr3s$>nsgIQVchetwG&C++$-l7&l6#vD(P%1RZCGpc^petQnH7{{3Z(n0WN#lZ]cK6yof!*2LdzsLZ?Zr+lo5J+Hrz[NKs5ZGzjYO4cCX4=zUs:227ixF";
        //readonly string SimulationLicenseCode = "0sywVlnRzumnD]{q^}X6v[gciix6J9xjVydm0iYNlpkr:fLTS(AwHV(ydQMBJ#xpe3/TKP1K@+u@Ou@r?#yOYy*8+BK(NeS{AL)]Wj5*0G8)(Osn[}m-8]2o&Fv]N=HJffMp!}euLYdFXwbl!AX:LxPHSi5hbuL";

        /// <summary>
        /// Initializes a new instance of ConsumerSimulationActor
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public ConsumerSimulationActor(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {
        }

        protected override async Task OnActivateAsync()
        {
            try
            {
                ConfigureStateMachine();

                if (_StateMachine.State == ConsumerSimulationStateType.None)
                {
                    await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.CreateActor);
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                throw;
            }
        }

        public async Task InitializeAsync(string appApiLicenseKey, string consumerLicenseCode)
        {
            try
            {
                if (_StateMachine.State == ConsumerSimulationStateType.ActorCreated)
                {
                    await _StateMachine.FireAsync(_InitializeTrigger, appApiLicenseKey, consumerLicenseCode);
                }

                else
                {
                    ActorEventSource.Current.ActorMessage(this, $"{nameof(ConsumerSimulationActor)} already initialized.");
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                throw;
            }
        }

        private async Task CreateToInitialized(string appApiLicenseKey, string consumerLicenseCode)
        {
            WriteTimedDebug("CreateToInitialized Entered");

            await RegisterReminderAsync(WorkflowReminderKey, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await StateManager.SetStateAsync(AppApiLicenseCodeKey, appApiLicenseKey).ConfigureAwait(false);
            await StateManager.SetStateAsync(ConsumerLicenseCodeKey, consumerLicenseCode).ConfigureAwait(false); 

            await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.RetrieveChannelGroups);
        }

        private async Task ProcessKillMe()
        {
            if (_StateMachine.IsInState(ConsumerSimulationStateType.DeadManWalking))
            {
                try
                {
                    var rnd = new Random();

                    //ICheckoutSessionManager proxy = ServiceProxy.Create<ICheckoutSessionManager>(
                    //    new Uri("fabric:/Deploy.Lazlo.Checkout.Api/Lazlo.SrvcFbrc.Services.CheckoutSessionManager"),
                    //    new Microsoft.ServiceFabric.Services.Client.ServicePartitionKey(
                    //            rnd.Next(0, 1023)));

                    //var killMe = await proxy.Archive(this.GetActorReference().ActorId.GetGuidId());
                }
                catch (Exception ex)
                {
                }
            }
        }

        public async Task RetrieveCheckoutStatus()
        {
            Uri requestUri = GetFullUri("api/v2/shopping/checkout/status");

            HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Get, requestUri);

            string appApiLicenseCode = await StateManager.GetStateAsync<string>(AppApiLicenseCodeKey).ConfigureAwait(false);
            string consumerLicenseCode = await StateManager.GetStateAsync<string>(ConsumerLicenseCodeKey).ConfigureAwait(false);
            string checkoutSessionLicenseCode = await StateManager.GetStateAsync<string>(ActionLicenseCodeKey).ConfigureAwait(false);

            httpreq.Headers.Add("lazlo-consumerlicensecode", consumerLicenseCode);
            httpreq.Headers.Add("lazlo-apilicensecode", appApiLicenseCode);
            httpreq.Headers.Add("lazlo-txlicensecode", checkoutSessionLicenseCode);

            var message = await _HttpClient.SendAsync(httpreq);

            string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (message.IsSuccessStatusCode)
            {
                var statusResponse = JsonConvert.DeserializeObject<SmartResponse<CheckoutStatusResponse>>(responseJson);

                List<EntitySecret> inProgressDownloads = await StateManager.GetOrAddStateAsync(InProgressDownloadsKey, new List<EntitySecret>());

                foreach(var item in statusResponse.Data.TicketStatuses)
                {
                    WriteTimedDebug($"DANGER!!!: {item.SasUri} {item.ValidationLicenseCode}");

                    if(!inProgressDownloads.Any(z => z.ValidationLicenseCode == item.ValidationLicenseCode) && item.GeneratedOn != null)
                    {
                        ActorId downloadActorId = new ActorId(item.ValidationLicenseCode);

                        IConsumerEntityDownloadActor downloadActor = ActorProxy.Create<IConsumerEntityDownloadActor>(downloadActorId, EntityDownloadServiceUri);

                        await downloadActor.InitalizeAsync(Id.GetGuidId(), consumerLicenseCode, item);

                        inProgressDownloads.Add(new EntitySecret { ValidationLicenseCode = item.ValidationLicenseCode });

                        await StateManager.SetStateAsync(InProgressDownloadsKey, inProgressDownloads).ConfigureAwait(false);
                    }
                }

                if(inProgressDownloads.All(z => z.Hash != null) && inProgressDownloads.Count == statusResponse.Data.TicketStatusCount)
                {
                    Guid posId = await StateManager.GetStateAsync<Guid>(PosDeviceActorIdKey);

                    ActorId posActorId = new ActorId(posId);

                    IPosDeviceSimulationActor posActor = ActorProxy.Create<IPosDeviceSimulationActor>(posActorId, PosDeviceServiceUri);

                    await posActor.ConsumerLeavesPos();

                    await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.MoveToTheBackOfTheLine);
                }

                else
                {
                    await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.WaitForTicketsToRender);
                }
            }

            else
            {
                await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.WaitForTicketsToRender);
            }
        }

        private async Task MoveToTheBackOfTheLine()
        {
            try
            {
                int txCount = await StateManager.AddOrUpdateStateAsync(TotalPurchasesKey, 0, (x, y) =>
                {
                    Debug.WriteLine(x);
                    return y + 1;
                });

                string appApiLicenseCode = await StateManager.GetStateAsync<string>(AppApiLicenseCodeKey).ConfigureAwait(false);

                Random random = new Random();

                int partitionIndex = random.Next(0, 4);

                ServicePartitionKey servicePartitionKey = new ServicePartitionKey(partitionIndex);

                ILineService proxy = ServiceProxy.Create<ILineService>(LineServiceUri, servicePartitionKey);

                await proxy.GetInLineAsync(appApiLicenseCode, Id.GetGuidId()).ConfigureAwait(false);

                await RemoveTransactionStatesAsync();

                await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.GetInLine);
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);

                await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.WaitForTicketsToRender);
            }
        }

        private async Task RemoveTransactionStatesAsync()
        {
            await StateManager.RemoveStateAsync(PosDeviceActorIdKey);
            await StateManager.RemoveStateAsync(ActionLicenseCodeKey);
            await StateManager.RemoveStateAsync(PosDeviceModeKey);
            await StateManager.RemoveStateAsync(InProgressDownloadsKey);
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                ActorEventSource.Current.ActorMessage(this, $"{nameof(ConsumerSimulationActor)} {Id} reminder recieved.");

                switch (reminderName)
                {
                    case KillMeReminderKey:
                        await ProcessKillMe();
                        break;

                    case WorkflowReminderKey:
                        await ProcessWorkflow();
                        break;
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
            }
        }

        private async Task<List<ChannelRequest>> CreateChannelSelections()
        {
            List<ChannelGroupDisplay> channelGroups = await StateManager.GetStateAsync<List<ChannelGroupDisplay>>(ChannelGroupsKey).ConfigureAwait(false);

            List<ChannelRequest> result = new List<ChannelRequest>();

            foreach (var channelRefId in channelGroups.SelectMany(z => z.Channels).Select(z => z.ChannelRefId))
            {
                ChannelRequest channelRequest = new ChannelRequest
                {
                    ChannelRefId = channelRefId,
                    ChannelSelections = new List<ChannelSelectionRequest>()
                };

                result.Add(channelRequest);
            }

            ChannelDisplay channelDisplay = channelGroups.RandomPick().Channels.RandomPick();

            // Going back to one channel request for now
            result = new List<ChannelRequest>
            {
                new ChannelRequest
                {
                    ChannelRefId = channelDisplay.ChannelRefId
                }
            };
                       
            return result;
        }

        private async Task RetrieveChannelGroupAsync()
        {
            Uri requestUri = GetFullUri("api/v2/brands/channelgroup/display");

            HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Get, requestUri);

            Guid correlationRefId = Guid.NewGuid();

            string consumerLicenseCode = await StateManager.GetStateAsync<string>(ConsumerLicenseCodeKey).ConfigureAwait(false);

            string appAPiLicenseCode = await StateManager.GetStateAsync<string>(AppApiLicenseCodeKey);

            httpreq.Headers.Add("lazlo-consumerlicensecode", consumerLicenseCode);
            httpreq.Headers.Add("lazlo-apilicensecode", appAPiLicenseCode);
            httpreq.Headers.Add("lazlo-correlationrefId", correlationRefId.ToString());

            HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

            string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

            var response = JsonConvert.DeserializeObject<SmartResponse<List<BrandWithChannelsDisplay>>>(responseJson);

            if (message.IsSuccessStatusCode)
            {
                List<ChannelGroupDisplay> channelGroups = response.Data.SelectMany(z => z.ChannelGroups).ToList();

                await StateManager.SetStateAsync(ChannelGroupsKey, channelGroups).ConfigureAwait(false);

                await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.GetInLine);
            }

            else
            {
                throw new CorrelationException($"Failed to set game: {message.StatusCode}") { CorrelationRefId = correlationRefId };
            }
        }

        private async Task CreateTicketCheckoutRequest()
        {
            try
            {
                Guid posId = await StateManager.GetStateAsync<Guid>(PosDeviceActorIdKey).ConfigureAwait(false);
                PosDeviceModes posDeviceMode = await StateManager.GetStateAsync<PosDeviceModes>(PosDeviceModeKey).ConfigureAwait(false);

                string checkoutLicenseCode;

                var existingTransaction = await StateManager.TryGetStateAsync<string>(ActionLicenseCodeKey).ConfigureAwait(false);

                if(existingTransaction.HasValue)
                {
                    checkoutLicenseCode = existingTransaction.Value;
                }

                else
                {
                    if (posDeviceMode == PosDeviceModes.ConsumerScans)
                    {
                        ActorId posActorId = new ActorId(posId);

                        IPosDeviceSimulationActor posActor = ActorProxy.Create<IPosDeviceSimulationActor>(posActorId, PosDeviceServiceUri);

                        checkoutLicenseCode = await posActor.ConsumerScansPos().ConfigureAwait(false);
                    }

                    else
                    {
                        checkoutLicenseCode = await RetrieveCheckoutLicenseCode().ConfigureAwait(false);
                    }

                    await StateManager.SetStateAsync(ActionLicenseCodeKey, checkoutLicenseCode).ConfigureAwait(false);
                    await StateManager.SaveStateAsync().ConfigureAwait(false);
                }

                var channelSelections = await CreateChannelSelections();

                if (channelSelections == null)
                {
                    return;
                }

                var checkoutRequest = new SmartRequest<CartCheckoutRequest>
                {
                    CreatedOn = DateTimeOffset.UtcNow,
                    Latitude = 34.072846D,
                    Longitude = -84.190285D,
                    Uuid = "A8C1048F-5A2B-4953-9C71-36581827AFE1",
                    Data = new CartCheckoutRequest
                    {
                        Cart = new CartRequest
                        {
                            ChannelRequests = channelSelections
                        },
                        //ChannelSelections = channelSelections,
                        //RetailerRefId = retailerRefId,
                        // this way we differentiate from client simulation and actor simulation
                        //SimulationType = SimulationType.Player |  SimulationType.Actor,
                        //PanelSelections = panels.Selections,
                        // shah1 of 12345
                        //SomethingIKnowCredentialCypherText = "8CB2237D0679CA88DB6464EAC60DA96345513964",
                        //SomethingIHaveCredentialCypherText = "",
                        SomethingIAmCredentialImageEncoded = "data:image/jpeg;base64,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AAYAAAAAAAEAMgAAAAEAWgAAAAYAAAAAAAEANQAAAAEALQAAAAYAAAAAAAE4QklNA/gAAAAAAHAAAP////////////////////////////8D6AAAAAD/////////////////////////////A+gAAAAA/////////////////////////////wPoAAAAAP////////////////////////////8D6AAAOEJJTQQAAAAAAAACAAE4QklNBAIAAAAAAAQAAAAAOEJJTQQwAAAAAAACAQE4QklNBC0AAAAAAAYAAQAAAAI4QklNBAgAAAAAABAAAAABAAACQAAAAkAAAAAAOEJJTQQeAAAAAAAEAAAAADhCSU0EGgAAAAADSQAAAAYAAAAAAAAAAAAAAJgAAACYAAAACgBVAG4AdABpAHQAbABlAGQALQAyAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAACYAAAAmAAAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAAAAAAAAABAAAAABAAAAAAAAbnVsbAAAAAIAAAAGYm91bmRzT2JqYwAAAAEAAAAAAABSY3QxAAAABAAAAABUb3AgbG9uZwAAAAAAAAAATGVmdGxvbmcAAAAAAAAAAEJ0b21sb25nAAAAmAAAAABSZ2h0bG9uZwAAAJgAAAAGc2xpY2VzVmxMcwAAAAFPYmpjAAAAAQAAAAAABXNsaWNlAAAAEgAAAAdzbGljZUlEbG9uZwAAAAAAAAAHZ3JvdXBJRGxvbmcAAAAAAAAABm9yaWdpbmVudW0AAAAMRVNsaWNlT3JpZ2luAAAADWF1dG9HZW5lcmF0ZWQAAAAAVHlwZWVudW0AAAAKRVNsaWNlVHlwZQAAAABJbWcgAAAABmJvdW5kc09iamMAAAABAAAAAAAAUmN0MQAAAAQAAAAAVG9wIGxvbmcAAAAAAAAAAExlZnRsb25nAAAAAAAAAABCdG9tbG9uZwAAAJgAAAAAUmdodGxvbmcAAACYAAAAA3VybFRFWFQAAAABAAAAAAAAbnVsbFRFWFQAAAABAAAAAAAATXNnZVRFWFQAAAABAAAAAAAGYWx0VGFnVEVYVAAAAAEAAAAAAA5jZWxsVGV4dElzSFRNTGJvb2wBAAAACGNlbGxUZXh0VEVYVAAAAAEAAAAAAAlo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AQD/4Q5faHR0cDovL25zLmFkb2JlLmNvbS94YXAvMS4wLwA8P3hwYWNrZXQgYmVnaW49Iu+7vyIgaWQ9Ilc1TTBNcENlaGlIenJlU3pOVGN6a2M5ZCI/PiA8eDp4bXBtZXRhIHhtbG5zOng9ImFkb2JlOm5zOm1ldGEvIiB4OnhtcHRrPSJBZG9iZSBYTVAgQ29yZSA1LjYtYzExMSA3OS4xNTgzMjUsIDIwMTUvMDkvMTAtMDE6MTA6MjAgICAgICAgICI+IDxyZGY6UkRGIHhtbG5zOnJkZj0iaHR0cDovL3d3dy53My5vcmcvMTk5OS8wMi8yMi1yZGYtc3ludGF4LW5zIyI+IDxyZGY6RGVzY3JpcHRpb24gcmRmOmFib3V0PSIiIHhtbG5zOnhtcD0iaHR0cDovL25zLmFkb2JlLmNvbS94YXAvMS4wLyIgeG1sbnM6eG1wTU09Imh0dHA6Ly9ucy5hZG9iZS5jb20veGFwLzEuMC9tbS8iIHhtbG5zOnN0RXZ0PSJodHRwOi8vbnMuYWRvYmUuY29tL3hhcC8xLjAvc1R5cGUvUmVzb3VyY2VFdmVudCMiIHhtbG5zOnBob3Rvc2hvcD0iaHR0cDovL25zLmFkb2JlLmNvbS9waG90b3Nob3AvMS4wLyIgeG1sbnM6ZGM9Imh0dHA6Ly9wdXJsLm9yZy9kYy9lbGVtZW50cy8xLjEvIiB4bXA6Q3JlYXRvclRvb2w9IkFkb2JlIFBob3Rvc2hvcCBDQyAyMDE1IChXaW5kb3dzKSIgeG1wOkNyZWF0ZURhdGU9IjIwMTUtMTItMDNUMTk6NDQ6NTArMDg6MDAiIHhtcDpNZXRhZGF0YURhdGU9IjIwMTUtMTItMDNUMTk6NDQ6NTArMDg6MDAiIHhtcDpNb2RpZnlEYXRlPSIyMDE1LTEyLTAzVDE5OjQ0OjUwKzA4OjAwIiB4bXBNTTpJbnN0YW5jZUlEPSJ4bXAuaWlkOjk1MTkyOWZmLTA1NjYtNGQ0Mi04NGI2LWE4NjQ1ZWJjNjA1OSIgeG1wTU06RG9jdW1lbnRJRD0iYWRvYmU6ZG9jaWQ6cGhvdG9zaG9wOjQyYWYxMTdlLTk5YjMtMTFlNS04MTM0LWZhZjRjZTRjOTAwOCIgeG1wTU06T3JpZ2luYWxEb2N1bWVudElEPSJ4bXAuZGlkOjIwMTBjNGVlLTA5MTktYjY0NS05NDk2LTExMWNlYzA4NjhhMSIgcGhvdG9zaG9wOkNvbG9yTW9kZT0iMyIgZGM6Zm9ybWF0PSJpbWFnZS9qcGVnIj4gPHhtcE1NOkhpc3Rvcnk+IDxyZGY6U2VxPiA8cmRmOmxpIHN0RXZ0OmFjdGlvbj0iY3JlYXRlZCIgc3RFdnQ6aW5zdGFuY2VJRD0ieG1wLmlpZDoyMDEwYzRlZS0wOTE5LWI2NDUtOTQ5Ni0xMTFjZWMwODY4YTEiIHN0RXZ0OndoZW49IjIwMTUtMTItMDNUMTk6NDQ6NTArMDg6MDAiIHN0RXZ0OnNvZnR3YXJlQWdlbnQ9IkFkb2JlIFBob3Rvc2hvcCBDQyAyMDE1IChXaW5kb3dzKSIvPiA8cmRmOmxpIHN0RXZ0OmFjdGlvbj0ic2F2ZWQiIHN0RXZ0Omluc3RhbmNlSUQ9InhtcC5paWQ6OTUxOTI5ZmYtMDU2Ni00ZDQyLTg0YjYtYTg2NDVlYmM2MDU5IiBzdEV2dDp3aGVuPSIyMDE1LTEyLTAzVDE5OjQ0OjUwKzA4OjAwIiBzdEV2dDpzb2Z0d2FyZUFnZW50PSJBZG9iZSBQaG90b3Nob3AgQ0MgMjAxNSAoV2luZG93cykiIHN0RXZ0OmNoYW5nZWQ9Ii8iLz4gPC9yZGY6U2VxPiA8L3htcE1NOkhpc3Rvcnk+IDxwaG90b3Nob3A6RG9jdW1lbnRBbmNlc3RvcnM+IDxyZGY6QmFnPiA8cmRmOmxpPjIzOEM3RDVDQTE0Rjg5QjgzNDAxMjczMTk3ODM0OTgwPC9yZGY6bGk+IDwvcmRmOkJhZz4gPC9waG90b3Nob3A6RG9jdW1lbnRBbmNlc3RvcnM+IDwvcmRmOkRlc2NyaXB0aW9uPiA8L3JkZjpSREY+IDwveDp4bXBtZXRhPiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAg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"
                    }
                };

                Guid correlationRefId = Guid.NewGuid();

                Uri requestUri = GetFullUri("api/v1/shopping/consumer/checkout");

                HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

                string consumerLicenseCode = await StateManager.GetStateAsync<string>(ConsumerLicenseCodeKey).ConfigureAwait(false);
                string appApiLicenseCode = await StateManager.GetStateAsync<string>(AppApiLicenseCodeKey).ConfigureAwait(false);

                httpreq.Headers.Add("lazlo-apilicensecode", appApiLicenseCode);
                httpreq.Headers.Add("lazlo-consumerlicensecode", consumerLicenseCode);
                httpreq.Headers.Add("lazlo-txlicensecode", checkoutLicenseCode);
                httpreq.Headers.Add("lazlo-correlationrefId", correlationRefId.ToString());

                string json = JsonConvert.SerializeObject(checkoutRequest);

                httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                DateTimeOffset opStart = DateTimeOffset.UtcNow;

                HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

                WriteTimedDebug($"Ticket Checkout request sent");

                string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

                var response = JsonConvert.DeserializeObject<SmartResponse<CheckoutResponse>>(responseJson);

                if (message.IsSuccessStatusCode)
                {
                    WriteTimedDebug($"Ticket Checkout Successful");

                    await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.WaitForTicketsToRender);
                }

                else
                {
                    throw new CorrelationException($"Ticket Checkout request failed: {message.StatusCode} {response.Error.Message}") { CorrelationRefId = correlationRefId };
                }
            }
            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.ApproachPos);   // Reset
            }
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

        private void WriteTimedDebug(string message)
        {
            Debug.WriteLine($"{DateTimeOffset.Now}: {message}");
        }

        private void WriteTimedDebug(Exception ex)
        {
            Debug.WriteLine($"{DateTimeOffset.Now}: {ex}");
        }

        private async Task<string> RetrieveCheckoutLicenseCode()
        {
            try
            {
                Uri requestUri = GetFullUri("api/v1/shopping/consumer/checkout/session");

                HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

                string consumerLicenseCode = await StateManager.GetStateAsync<string>(ConsumerLicenseCodeKey).ConfigureAwait(false);
                string appApiLicenseCode = await StateManager.GetStateAsync<string>(AppApiLicenseCodeKey).ConfigureAwait(false);

                httpreq.Headers.Add("lazlo-apilicensecode", appApiLicenseCode);
                httpreq.Headers.Add("lazlo-consumerlicensecode", consumerLicenseCode);

                SmartRequest<object> req = new SmartRequest<object> { };

                string json = JsonConvert.SerializeObject(req);

                httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

                string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

                SmartResponse<CheckoutSessionCreateResponse> response = JsonConvert.DeserializeObject<SmartResponse<CheckoutSessionCreateResponse>>(responseJson);

                if (message.IsSuccessStatusCode)
                {
                    return response.Data.CheckoutLicenseCode;
                }

                else
                {
                    throw new Exception(response.Error.Message);
                }
            }

            catch (Exception ex)
            {
                WriteTimedDebug(ex);
                throw;
            }
        }

        public async Task BeginTransaction(Guid posDeviceActorId, PosDeviceModes posDeviceMode)
        {
            if(_StateMachine.State == ConsumerSimulationStateType.WaitingInLine)
            {
                await _StateMachine.FireAsync(_AssignPosTrigger, posDeviceActorId, posDeviceMode).ConfigureAwait(false);
            }

            else
            {
                throw new Exception("Consumer is not waiting in line");
            }
        }

        private async Task AssignPosAsync(Guid posDeviceActorId, PosDeviceModes posDeviceMode)
        {
            await StateManager.SetStateAsync(PosDeviceActorIdKey, posDeviceActorId).ConfigureAwait(false);
            await StateManager.SetStateAsync(PosDeviceModeKey, posDeviceMode).ConfigureAwait(false);

            await _StateMachine.FireAsync(ConsumerSimulationWorkflowActions.ApproachPos);
        }

        public async Task<string> PosScansConsumer()
        {
            ConditionalValue<string> actionLicenseCheck = await StateManager.TryGetStateAsync<string>(ActionLicenseCodeKey);

            return actionLicenseCheck.HasValue ? actionLicenseCheck.Value : null;
        }

        public async Task UpdateDownloadStatusAsync(EntitySecret entitySecret)
        {
            WriteTimedDebug($"Updating download status: {entitySecret.ValidationLicenseCode}");

            List <EntitySecret> inProgressDownloads = await StateManager.GetStateAsync<List<EntitySecret>>(InProgressDownloadsKey);

            inProgressDownloads.RemoveAll(z => z.ValidationLicenseCode == entitySecret.ValidationLicenseCode);

            inProgressDownloads.Add(entitySecret);

            await StateManager.SetStateAsync(InProgressDownloadsKey, inProgressDownloads).ConfigureAwait(false);            
        }
    }
}
