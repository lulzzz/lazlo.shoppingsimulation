using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Actors.Client;
using Lazlo.ShoppingSimulation.Common.Interfaces;
using Lazlo.ShoppingSimulation.Common;
using System.Diagnostics;
using System.Net.Http;
using Newtonsoft.Json;
using Lazlo.Common.Responses;
using Lazlo.Common.Models;
using Lazlo.Common;
using System.IO;
using Lazlo.Gaming.Random;
using Lazlo.Common.Requests;

namespace Lazlo.ShoppingSimulation.PosDeviceSimulationActor
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
    internal class PosDeviceSimulationActor : Actor, IPosDeviceSimulationActor, IRemindable
    {
        static readonly Uri ConsumerServiceUri = new Uri("fabric:/Deploy.Lazlo.ShoppingSimulation/ConsumerSimulationActorService");

        const bool _UseLocalHost = false;
        string _UriBase = "devshopapi.services.32point6.com";

        const string InitializedKey = "InitializedKey";
        const string LicenseCodeKey = "LicenseCodeKey";
        const string PosDeviceModeKey = "PosDeviceModeKey";
        const string ReminderName = "ReminderName";
        const string StartupStatusKey = "StartupStatusKey";
        const string PlayerLicenseCodeKey = "PlayerLicenseCodeKey";

        protected HttpClient _HttpClient = new HttpClient();

        //const string RetailerRefIdKey = "RetailerRefIdKey"; //TODO Pull in as part of Initialization

        double _Latitude = 42.129224;       //TODO pull from init
        double _Longitude = -80.085059;

        readonly string AuthorityLicenseCode = "0rsVpol+brlnpe@m@?K1y?WA$Cw?v*hbE[hv(5u*A5zr3s$>nsgIQVchetwG&C++$-l7&l6#vD(P%1RZCGpc^petQnH7{{3Z(n0WN#lZ]cK6yof!*2LdzsLZ?Zr+lo5J+Hrz[NKs5ZGzjYO4cCX4=zUs:227ixF";
        readonly string SimulationLicenseCode = "0sywVlnRzumnD]{q^}X6v[gciix6J9xjVydm0iYNlpkr:fLTS(AwHV(ydQMBJ#xpe3/TKP1K@+u@Ou@r?#yOYy*8+BK(NeS{AL)]Wj5*0G8)(Osn[}m-8]2o&Fv]N=HJffMp!}euLYdFXwbl!AX:LxPHSi5hbuL";

        /// <summary>
        /// Initializes a new instance of PosDeviceSimulationActor
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public PosDeviceSimulationActor(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {
        }

        public async Task InitializeAsync(string licenseCode, PosDeviceModes posDeviceMode)
        {
            bool isInitialized = await StateManager.GetOrAddStateAsync<bool>(InitializedKey, false).ConfigureAwait(false);

            if (isInitialized)
            {
                return;
            }

            await RegisterReminderAsync(ReminderName, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)).ConfigureAwait(false);

            await StateManager.SetStateAsync(StartupStatusKey, "Created").ConfigureAwait(false);
            await StateManager.SetStateAsync(LicenseCodeKey, licenseCode).ConfigureAwait(false);
            await StateManager.SetStateAsync(PosDeviceModeKey, posDeviceMode).ConfigureAwait(false);
            await StateManager.SetStateAsync(InitializedKey, true).ConfigureAwait(false);

            Debug.WriteLine("POS Device Initialized");
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                string status = await StateManager.GetStateAsync<string>(StartupStatusKey);

                switch (status)
                {
                    case "Created":
                        await CreateConsumerAsync();

                        await StateManager.SetStateAsync(StartupStatusKey, "ConsumerCreated").ConfigureAwait(false);
                        break;
                }
            }

            catch (Exception ex)
            {
                Debug.WriteLine(ex);
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

        private static byte[] GetImageBytes(string imageName)
        {
            using (Stream stream = typeof(PosDeviceSimulationActor).Assembly.GetManifestResourceStream($"Lazlo.ShoppingSimulation.PosDeviceSimulationActor.Images.{imageName}"))
            {
                byte[] buffer = new byte[stream.Length];

                stream.Read(buffer, 0, buffer.Length);

                return buffer;
            }
        }

        private async Task CreateConsumerAsync()
        {
            byte[] selfieBytes = GetImageBytes("christina.png");

            string selfieBase64 = Convert.ToBase64String(selfieBytes);

            CryptoRandom random = new CryptoRandom();

            int age = random.Next(12, 115);

            SmartRequest<PlayerRegisterRequest> req = new SmartRequest<PlayerRegisterRequest>
            {
                CorrelationRefId = Guid.NewGuid(),
                CreatedOn = DateTimeOffset.UtcNow,
                Latitude = 34.072846D,
                Longitude = -84.190285D,
                Data = new PlayerRegisterRequest
                {
                    CountryCode = "US",
                    LanguageCode = "en-US",
                    SelfieBase64 = selfieBase64,
                    Data = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("age", age.ToString())
                    }
                }
                ,
                Uuid = $"{Guid.NewGuid()}"
            };

            Uri requestUri = GetFullUri("api/v3/player/registration");
            HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Post, requestUri);

            //httpreq.Headers.Add("Lazlo-SimulationLicenseCode", SimulationLicenseCode);
            //httpreq.Headers.Add("Lazlo-AuthorityLicenseCode", AuthorityLicenseCode);
            httpreq.Headers.Add("lazlo-correlationrefId", req.CorrelationRefId.ToString());

            string json = JsonConvert.SerializeObject(req);

            httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            HttpResponseMessage message = await _HttpClient.SendAsync(httpreq).ConfigureAwait(false);

            string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (message.IsSuccessStatusCode)
            {
                if (age < 18)
                {
                    throw new CorrelationException("Allowed to register a player under 18") { CorrelationRefId = req.CorrelationRefId };
                }

                var statusResponse = JsonConvert.DeserializeObject<SmartResponse<ConsumerRegisterResponse>>(responseJson);

                ActorId consumerActorId = new ActorId(Guid.NewGuid());

                IConsumerSimulationActor consumerActor = ActorProxy.Create<IConsumerSimulationActor>(consumerActorId, ConsumerServiceUri);

                await consumerActor.InitializeAsync(statusResponse.Data.ConsumerLicenseCode, Id.GetGuidId()).ConfigureAwait(false);
            }

            else
            {
                if (age >= 18)
                {
                    throw new CorrelationException($"Player registration failed: {message.StatusCode} {responseJson}") { CorrelationRefId = req.CorrelationRefId };
                }

                WriteTimedDebug("Player not registered due to age restriction");
            }
        }

        private void WriteTimedDebug(string message)
        {
            Debug.WriteLine($"{DateTimeOffset.Now}: {message}");
        }

        //private async Task SetRetailer()
        //{
        //    Uri requestUri = GetFullUri($"api/v2/retailers/display/bylocation/100/{_Latitude}/{_Longitude}");

        //    //SmartRequest<string> req = new SmartRequest<string>
        //    //{
        //    //    CorrelationRefId = Guid.NewGuid(),
        //    //    CreatedOn = DateTimeOffset.UtcNow,
        //    //    Data = null,
        //    //    Latitude = _Latitude,
        //    //    Longitude = _Longitude,
        //    //    Uuid = Guid.NewGuid().ToString()
        //    //};

        //    Guid correlationRefId = Guid.NewGuid();

        //    HttpRequestMessage httpreq = new HttpRequestMessage(HttpMethod.Get, requestUri);

        //    httpreq.Headers.Add("Lazlo-SimulationLicenseCode", SimulationLicenseCode);
        //    httpreq.Headers.Add("Lazlo-AuthorityLicenseCode", AuthorityLicenseCode);
        //    httpreq.Headers.Add("Lazlo-CorrelationRefId", correlationRefId.ToString());

        //    //string json = JsonConvert.SerializeObject(req);

        //    //httpreq.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        //    HttpResponseMessage message = await _Client.SendAsync(httpreq).ConfigureAwait(false);

        //    if (message.IsSuccessStatusCode)
        //    {
        //        string responseJson = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

        //        var response = JsonConvert.DeserializeObject<SmartResponse<List<RetailerDisplay<Beacon>>>>(responseJson);

        //        await StateManager.SetStateAsync(RetailerRefIdKey, response.Data.First().RetailerRefId).ConfigureAwait(false);

        //        Debug.WriteLine("Retailer Set");
        //    }

        //    else
        //    {
        //        string err = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

        //        throw new CorrelationException($"SetRetailer failed: {message.StatusCode} {err}") { CorrelationRefId = correlationRefId };
        //    }
        //}
    }
}
