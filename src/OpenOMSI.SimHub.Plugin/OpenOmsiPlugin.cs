using GameReaderCommon;
using GameReaderCommon.Feedback;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SimHub.Plugins;
using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace OpenOMSI.SimHub
{
    [PluginDescription("Receives openOMSI Lua telemetry over local UDP and exposes it as SimHub properties.")]
    [PluginAuthor("OpenOMSI SimHub Bridge")]
    [PluginName("OpenOMSI Telemetry")]
    public sealed class OpenOmsiPlugin : IPlugin, IRTDataPlugin
    {
        private const int Port = 44444;
        private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(1.0);

        private readonly TelemetryPacket emptyPacket = new TelemetryPacket();
        private volatile bool stopping;
        private volatile bool receiverRunning;
        private TelemetryPacket latestPacket;
        private long latestPacketUtcTicks;
        private int firstPacketLogged;
        private int normalizationErrorLogged;
        private UdpClient udpClient;
        private Thread receiverThread;
        private TabletWebServer tabletWebServer;
        private readonly TabletCommandBridge tabletCommandBridge = new TabletCommandBridge();

        public PluginManager PluginManager { get; set; }

        private TelemetryPacket Current
        {
            get { return Interlocked.CompareExchange(ref latestPacket, null, null) ?? emptyPacket; }
        }

        private double PacketAgeMilliseconds
        {
            get
            {
                long ticks = Interlocked.Read(ref latestPacketUtcTicks);
                if (ticks == 0) return -1;
                DateTime received = new DateTime(ticks, DateTimeKind.Utc);
                return Math.Max(0, (DateTime.UtcNow - received).TotalMilliseconds);
            }
        }

        private bool IsConnected
        {
            get { return receiverRunning && Current.Connected && PacketAgeMilliseconds >= 0 && PacketAgeMilliseconds < ConnectionTimeout.TotalMilliseconds; }
        }

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            RegisterProperties();
            StartReceiver();
            StartTabletWebServer();
            global::SimHub.Logging.Current.Info("OpenOMSI Telemetry plugin initialized on UDP 127.0.0.1:" + Port);
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            try
            {
                PublishNormalizedGameData(ref data);
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref normalizationErrorLogged, 1) == 0)
                {
                    global::SimHub.Logging.Current.Error("OpenOMSI Telemetry normalization failed: " + ex);
                }
            }
        }

        public void End(PluginManager pluginManager)
        {
            stopping = true;
            try { if (tabletWebServer != null) tabletWebServer.Stop(); } catch { }
            try { if (udpClient != null) udpClient.Close(); } catch { }
            try { if (receiverThread != null && receiverThread.IsAlive) receiverThread.Join(1000); } catch { }
            receiverRunning = false;
            global::SimHub.Logging.Current.Info("OpenOMSI Telemetry plugin stopped");
        }

        private void StartTabletWebServer()
        {
            try
            {
                tabletWebServer = new TabletWebServer(44445, GetTabletStateJson, DispatchTabletCommand);
                tabletWebServer.Start();
                global::SimHub.Logging.Current.Info("OpenOMSI tablet dashboard available on local network, TCP port 44445");
            }
            catch (Exception ex)
            {
                global::SimHub.Logging.Current.Warn("OpenOMSI tablet dashboard could not start: " + ex.Message);
            }
        }

        private TabletCommandResult DispatchTabletCommand(string json)
        {
            if (!IsConnected) return new TabletCommandResult(false, "Warte auf OpenOMSI.");
            return tabletCommandBridge.Dispatch(json, Current.ControlProfile);
        }

        private string GetTabletStateJson()
        {
            return JsonConvert.SerializeObject(new
            {
                connected = IsConnected,
                packetAgeMs = PacketAgeMilliseconds,
                data = Current
            }, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
        }

        private void StartReceiver()
        {
            try
            {
                udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
                udpClient.Client.ReceiveTimeout = 500;
                receiverThread = new Thread(ReceiveLoop);
                receiverThread.Name = "OpenOMSI SimHub UDP receiver";
                receiverThread.IsBackground = true;
                receiverRunning = true;
                receiverThread.Start();
            }
            catch (Exception ex)
            {
                receiverRunning = false;
                global::SimHub.Logging.Current.Error("OpenOMSI Telemetry could not listen on UDP port " + Port + ": " + ex.Message);
            }
        }

        private void ReceiveLoop()
        {
            IPEndPoint sender = new IPEndPoint(IPAddress.Loopback, 0);
            while (!stopping)
            {
                try
                {
                    byte[] bytes = udpClient.Receive(ref sender);
                    if (!IPAddress.IsLoopback(sender.Address)) continue;

                    string json = Encoding.UTF8.GetString(bytes);
                    TelemetryPacket packet = JsonConvert.DeserializeObject<TelemetryPacket>(json);
                    if (packet == null || packet.Protocol != "openomsi-simhub" || packet.ProtocolVersion != 1) continue;

                    Interlocked.Exchange(ref latestPacket, packet);
                    Interlocked.Exchange(ref latestPacketUtcTicks, DateTime.UtcNow.Ticks);
                    if (Interlocked.Exchange(ref firstPacketLogged, 1) == 0)
                    {
                        global::SimHub.Logging.Current.Info(
                            "OpenOMSI Telemetry received first packet: " +
                            (packet.Vehicle ?? "unknown vehicle") + ", sequence " + packet.Sequence);
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.TimedOut && !stopping)
                    {
                        global::SimHub.Logging.Current.Warn("OpenOMSI Telemetry UDP error: " + ex.Message);
                    }
                }
                catch (ObjectDisposedException)
                {
                    if (!stopping) global::SimHub.Logging.Current.Warn("OpenOMSI Telemetry UDP socket closed unexpectedly");
                }
                catch (Exception ex)
                {
                    if (!stopping) global::SimHub.Logging.Current.Warn("OpenOMSI Telemetry ignored an invalid packet: " + ex.Message);
                }
            }
            receiverRunning = false;
        }

        private void RegisterProperties()
        {
            this.AttachDelegate("OpenOMSI.ReceiverRunning", () => receiverRunning);
            this.AttachDelegate("OpenOMSI.Connected", () => IsConnected);
            this.AttachDelegate("OpenOMSI.PacketAgeMs", () => PacketAgeMilliseconds);
            this.AttachDelegate("OpenOMSI.Sequence", () => Current.Sequence);
            this.AttachDelegate("OpenOMSI.GameTimeSeconds", () => Current.GameTimeSeconds);
            this.AttachDelegate("OpenOMSI.Paused", () => Current.Paused);
            this.AttachDelegate("OpenOMSI.Vehicle", () => Current.Vehicle ?? "");
            this.AttachDelegate("OpenOMSI.Manufacturer", () => Current.Manufacturer ?? "");
            this.AttachDelegate("OpenOMSI.Model", () => Current.Model ?? "");
            this.AttachDelegate("OpenOMSI.ControlProfile", () => Current.ControlProfile ?? "none");
            this.AttachDelegate("OpenOMSI.Map", () => Current.Map ?? "");
            this.AttachDelegate("OpenOMSI.SpeedKmh", () => Current.SpeedKmh);
            this.AttachDelegate("OpenOMSI.SpeedLimitKmh", () => Current.SpeedLimitKmh);
            this.AttachDelegate("OpenOMSI.Throttle", () => Current.Throttle);
            this.AttachDelegate("OpenOMSI.Brake", () => Current.Brake);
            this.AttachDelegate("OpenOMSI.SteeringRad", () => Current.SteeringRad);
            this.AttachDelegate("OpenOMSI.SteeringNormalized", () => Current.SteeringNormalized);
            this.AttachDelegate("OpenOMSI.EngineRpm", () => Current.EngineRpm);
            this.AttachDelegate("OpenOMSI.EngineRunning", () => Current.EngineRunning);
            this.AttachDelegate("OpenOMSI.EngineTemperatureC", () => Current.EngineTemperatureC);
            this.AttachDelegate("OpenOMSI.FuelLiters", () => Current.FuelLiters);
            this.AttachDelegate("OpenOMSI.FuelPercent", () => Current.FuelPercent);
            this.AttachDelegate("OpenOMSI.Gear", () => Current.Gear);
            this.AttachDelegate("OpenOMSI.GearSelection", () => Current.GearSelection);
            this.AttachDelegate("OpenOMSI.TransmissionType", () => Current.TransmissionType ?? "");
            this.AttachDelegate("OpenOMSI.GearSelector", () => Current.GearSelector ?? "");
            this.AttachDelegate("OpenOMSI.Retarder", () => Current.Retarder);
            this.AttachDelegate("OpenOMSI.ParkingBrake", () => Current.ParkingBrake);
            this.AttachDelegate("OpenOMSI.StopBrake", () => Current.StopBrake);
            this.AttachDelegate("OpenOMSI.AbsActive", () => Current.AbsActive);
            this.AttachDelegate("OpenOMSI.AirPressure1Bar", () => Current.AirPressure1Bar);
            this.AttachDelegate("OpenOMSI.AirPressure2Bar", () => Current.AirPressure2Bar);
            this.AttachDelegate("OpenOMSI.Door0", () => Current.Door0);
            this.AttachDelegate("OpenOMSI.Door1", () => Current.Door1);
            this.AttachDelegate("OpenOMSI.Door2", () => Current.Door2);
            this.AttachDelegate("OpenOMSI.Door3", () => Current.Door3);
            this.AttachDelegate("OpenOMSI.Door4", () => Current.Door4);
            this.AttachDelegate("OpenOMSI.Door5", () => Current.Door5);
            this.AttachDelegate("OpenOMSI.FrontDoorOpen", () => Current.FrontDoorOpen);
            this.AttachDelegate("OpenOMSI.MiddleDoorOpen", () => Current.MiddleDoorOpen);
            this.AttachDelegate("OpenOMSI.RearDoorOpen", () => Current.RearDoorOpen);
            this.AttachDelegate("OpenOMSI.DoorRelease", () => Current.DoorRelease);
            this.AttachDelegate("OpenOMSI.StopRequested", () => Current.StopRequested);
            this.AttachDelegate("OpenOMSI.TurnLeft", () => Current.TurnLeft);
            this.AttachDelegate("OpenOMSI.TurnRight", () => Current.TurnRight);
            this.AttachDelegate("OpenOMSI.Hazards", () => Current.Hazards);
            this.AttachDelegate("OpenOMSI.BrakeLight", () => Current.BrakeLight);
            this.AttachDelegate("OpenOMSI.PositionLights", () => Current.PositionLights);
            this.AttachDelegate("OpenOMSI.LowBeam", () => Current.LowBeam);
            this.AttachDelegate("OpenOMSI.HighBeam", () => Current.HighBeam);
            this.AttachDelegate("OpenOMSI.HeadlightsLevel", () => Current.HeadlightsLevel);
            this.AttachDelegate("OpenOMSI.ReverseLight", () => Current.ReverseLight);
            this.AttachDelegate("OpenOMSI.Wipers", () => Current.Wipers);
            this.AttachDelegate("OpenOMSI.WiperPosition", () => Current.WiperPosition);
            this.AttachDelegate("OpenOMSI.Line", () => Current.Line ?? "");
            this.AttachDelegate("OpenOMSI.Tour", () => Current.Tour ?? "");
            this.AttachDelegate("OpenOMSI.Trip", () => Current.Trip);
            this.AttachDelegate("OpenOMSI.Trips", () => Current.Trips);
            this.AttachDelegate("OpenOMSI.TripName", () => Current.TripName ?? "");
            this.AttachDelegate("OpenOMSI.Destination", () => Current.Destination ?? "");
            this.AttachDelegate("OpenOMSI.NextStop", () => Current.NextStop ?? "");
            this.AttachDelegate("OpenOMSI.NextStopNumber", () => Current.NextStopNumber);
            this.AttachDelegate("OpenOMSI.NextStopArrival", () => Current.NextStopArrival);
            this.AttachDelegate("OpenOMSI.NextStopDeparture", () => Current.NextStopDeparture);
            this.AttachDelegate("OpenOMSI.DelaySeconds", () => Current.DelaySeconds);
            this.AttachDelegate("OpenOMSI.Passengers", () => Current.Passengers);
            this.AttachDelegate("OpenOMSI.PositionX", () => Current.PositionX);
            this.AttachDelegate("OpenOMSI.PositionY", () => Current.PositionY);
            this.AttachDelegate("OpenOMSI.PositionZ", () => Current.PositionZ);
            this.AttachDelegate("OpenOMSI.HeadingDeg", () => Current.HeadingDeg);
            this.AttachDelegate("OpenOMSI.ArticulationRad", () => Current.ArticulationRad);
        }

        private void PublishNormalizedGameData(ref GameData data)
        {
            if (!IsConnected)
            {
                if (string.Equals(data.GameName, "OpenOMSI", StringComparison.OrdinalIgnoreCase))
                {
                    SetGameValue(data, "GameRunning", false);
                }
                return;
            }

            TelemetryPacket packet = Current;
            // Keep the StatusData instance created by SimHub's Custom Game
            // manager. Replacing it with StatusData<TelemetryPacket> breaks
            // SimHub components which expect their original generic Raw type.
            StatusDataBase normalized = data.NewData;
            if (normalized == null)
            {
                return;
            }
            SetStatusValue(normalized, "PacketTime", DateTime.Now);

            SetStatusValue(normalized, "CarId", packet.Model ?? "OpenOMSI Bus");
            SetStatusValue(normalized, "CarModel", packet.Vehicle ?? packet.Model ?? "OpenOMSI Bus");
            SetStatusValue(normalized, "CarClass", "Bus");
            SetStatusValue(normalized, "TrackId", packet.Map ?? "");
            SetStatusValue(normalized, "TrackName", packet.Map ?? "");
            SetStatusValue(normalized, "CarCoordinates", new[] { packet.PositionX, packet.PositionY, packet.PositionZ });

            SetStatusValue(normalized, "SpeedKmh", packet.SpeedKmh);
            SetStatusValue(normalized, "FilteredSpeedKmh", packet.SpeedKmh);
            SetStatusValue(normalized, "SpeedMph", packet.SpeedKmh * 0.621371192);
            SetStatusValue(normalized, "GroundSpeedKmH", (double?)packet.SpeedKmh);
            SetStatusValue(normalized, "SpeedLocal", packet.SpeedKmh);
            SetStatusValue(normalized, "SpeedLocalUnit", "km/h");
            SetStatusValue(normalized, "Throttle", packet.Throttle * 100.0);
            SetStatusValue(normalized, "Brake", packet.Brake * 100.0);
            SetStatusValue(normalized, "Clutch", 0.0);
            SetStatusValue(normalized, "Handbrake", packet.ParkingBrake ? 100.0 : 0.0);

            double feedbackRpmPercent = FeedbackRpmPercent(packet);
            double displayedRpm = packet.EngineRpm > 50.0 ? packet.EngineRpm : feedbackRpmPercent * 2500.0;
            SetStatusValue(normalized, "Rpms", displayedRpm);
            SetStatusValue(normalized, "FilteredRpms", displayedRpm);
            SetStatusValue(normalized, "MaxRpm", 2500.0);
            SetStatusValue(normalized, "Redline", 2200.0);
            SetStatusValue(normalized, "CarSettings_MaxRPM", 2500.0);
            SetStatusValue(normalized, "CarSettings_RedLineRPM", 2200.0);
            SetStatusValue(normalized, "CarSettings_CurrentDisplayedRPMPercent", feedbackRpmPercent * 100.0);
            bool driveActive = packet.EngineRunning || Math.Abs(packet.SpeedKmh) > 0.5;
            SetStatusValue(normalized, "EngineStarted", driveActive ? 1 : 0);
            SetStatusValue(normalized, "EngineIgnitionOn", driveActive ? 1 : 0);
            SetStatusValue(normalized, "WaterTemperature", packet.EngineTemperatureC);

            // Shake-It does not consume Rpms directly. Its RPM effect is fed by
            // StatusDataBase.FeedbackData.RPMPercent, even though the regular
            // dashboard properties already contain the correct engine speed.
            FeedbackData feedback = new FeedbackData();
            feedback.RPMPercent = feedbackRpmPercent;
            feedback.Speed = packet.SpeedKmh;
            feedback.DefaultMaxSpeed = 80.0;
            SetStatusValue(normalized, "FeedbackData", feedback);

            SetStatusValue(normalized, "Fuel", packet.FuelLiters);
            SetStatusValue(normalized, "FuelRaw", packet.FuelLiters);
            SetStatusValue(normalized, "FuelPercent", packet.FuelPercent);
            SetStatusValue(normalized, "MaxFuel", 250.0);
            SetStatusValue(normalized, "FuelUnit", "L");

            SetStatusValue(normalized, "Gear", NormalizedGear(packet));
            SetStatusValue(normalized, "ABSActive", packet.AbsActive ? 1 : 0);
            SetStatusValue(normalized, "TurnIndicatorLeft", packet.TurnLeft ? 1 : 0);
            SetStatusValue(normalized, "TurnIndicatorRight", packet.TurnRight ? 1 : 0);

            SetGameValue(data, "GameName", "OpenOMSI");
            data.GameReaderVariant = "OpenOMSI UDP Bridge";
            SetGameValue(data, "GameRunning", true);
            data.RunningGameProcessDetected = true;
            SetGameValue(data, "GamePaused", packet.Paused);
            SetGameValue(data, "GameInMenu", false);
            SetGameValue(data, "FrameTime", DateTime.Now);
            SetGameValue(data, "FrameTimeUTC", DateTime.UtcNow);
            normalizationErrorLogged = 0;
        }

        private static string NormalizedGear(TelemetryPacket packet)
        {
            if (string.Equals(packet.TransmissionType, "electric", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(packet.GearSelector, "R", StringComparison.OrdinalIgnoreCase)) return "R";
                if (string.Equals(packet.GearSelector, "N", StringComparison.OrdinalIgnoreCase)) return "N";
                if (string.Equals(packet.GearSelector, "D", StringComparison.OrdinalIgnoreCase)) return "D";
                if (packet.GearSelection <= 0.5) return "R";
                if (packet.GearSelection <= 1.5) return "N";
                return "D";
            }

            if (string.Equals(packet.TransmissionType, "manual", StringComparison.OrdinalIgnoreCase))
            {
                if (packet.Gear < -0.5) return "R";
                if (Math.Abs(packet.Gear) < 0.5) return "N";
                return Math.Round(packet.Gear).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (string.Equals(packet.TransmissionType, "automatic", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(packet.GearSelector, "R", StringComparison.OrdinalIgnoreCase)) return "R";
                if (string.Equals(packet.GearSelector, "N", StringComparison.OrdinalIgnoreCase)) return "N";
                if (string.Equals(packet.GearSelector, "D", StringComparison.OrdinalIgnoreCase)) return "D";
                if (packet.GearSelection <= 0.5) return "R";
                if (packet.GearSelection <= 1.5) return "N";
                return "D";
            }

            // Compatibility fallback for older Lua senders.
            if (string.Equals(packet.GearSelector, "R", StringComparison.OrdinalIgnoreCase)) return "R";
            if (string.Equals(packet.GearSelector, "N", StringComparison.OrdinalIgnoreCase)) return "N";
            if (string.Equals(packet.GearSelector, "D", StringComparison.OrdinalIgnoreCase)) return "D";
            if (string.Equals(packet.GearSelector, "E", StringComparison.OrdinalIgnoreCase)) return "E";
            if (packet.Gear > 0) return Math.Round(packet.Gear).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return packet.GearSelector ?? "N";
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        private static double FeedbackRpmPercent(TelemetryPacket packet)
        {
            if (packet.EngineRpm > 50.0)
            {
                return Clamp01(packet.EngineRpm / 2500.0);
            }

            // Electric buses commonly expose no combustion-engine RPM. A quiet,
            // speed-dependent virtual RPM keeps Shake-It alive while moving.
            double speed = Math.Abs(packet.SpeedKmh);
            if (speed <= 0.5) return 0.0;
            return 0.05 + Clamp01(speed / 60.0) * 0.15;
        }

        private static void SetStatusValue(StatusDataBase target, string propertyName, object value)
        {
            FieldInfo field = typeof(StatusDataBase).GetField(
                "<" + propertyName + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(typeof(StatusDataBase).FullName, propertyName);
            field.SetValue(target, value);
        }

        private static void SetGameValue(GameData target, string propertyName, object value)
        {
            FieldInfo field = typeof(GameData).GetField(
                "<" + propertyName + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(typeof(GameData).FullName, propertyName);
            field.SetValue(target, value);
        }
    }
}
