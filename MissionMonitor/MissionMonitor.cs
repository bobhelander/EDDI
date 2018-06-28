using Eddi;
using EddiDataDefinitions;
using EddiDataProviderService;
using EddiEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Threading;
using Utilities;

namespace EddiMissionMonitor
{
    /**
     * Monitor missions for the commander
     */
    public class MissionMonitor : EDDIMonitor
    {
        // Keep track of status
        private bool running;

        // Observable collection for us to handle changes
        public ObservableCollection<Mission> missions { get; private set; }

        public int missionsCount;
        public int? missionWarning;
        public string missionsRouteList = String.Empty;
        public decimal missionsRouteDistance = 0;

        private static readonly object missionsLock = new object();
        public event EventHandler MissionUpdatedEvent;

        public string MonitorName()
        {
            return "Mission monitor";
        }

        public string LocalizedMonitorName()
        {
            return Properties.MissionMonitor.mission_monitor_name;
        }

        public string MonitorVersion()
        {
            return "1.0.0";
        }

        public string MonitorDescription()
        {
            return Properties.MissionMonitor.mission_monitor_desc;
        }

        public bool IsRequired()
        {
            return true;
        }

        public MissionMonitor()
        {
            missions = new ObservableCollection<Mission>();
            BindingOperations.CollectionRegistering += Missions_CollectionRegistering;
            initializeMissionMonitor();
        }

        public void initializeMissionMonitor(MissionMonitorConfiguration configuration = null)
        {
            readMissions(configuration);
            Logging.Info("Initialised " + MonitorName() + " " + MonitorVersion());
        }

        private void Missions_CollectionRegistering(object sender, CollectionRegisteringEventArgs e)
        {
            if (Application.Current != null)
            {
                // Synchronize this collection between threads
                BindingOperations.EnableCollectionSynchronization(missions, missionsLock);
            }
            else
            {
                // If started from VoiceAttack, the dispatcher is on a different thread. Invoke synchronization there.
                Dispatcher.CurrentDispatcher.Invoke(() => { BindingOperations.EnableCollectionSynchronization(missions, missionsLock); });
            }
        }
        public bool NeedsStart()
        {
            return true;
        }

        public void Start()
        {
            _start();
        }

        public void Stop()
        {
            running = false;
        }

        public void Reload()
        {
            readMissions();
            Logging.Info("Reloaded " + MonitorName() + " " + MonitorVersion());

        }

        public void _start()
        {
            running = true;

            while (running)
            {
                foreach (Mission mission in missions.ToList())
                {
                    if (mission.expiry != null && mission.statusEDName != "Failed")
                    {
                        TimeSpan span = (DateTime)mission.expiry?.ToLocalTime() - DateTime.Now;
                        mission.timeremaining = span.Days.ToString() + "D " + span.Hours.ToString() + "H " + span.Minutes.ToString() + "MIN";
                        if (mission.expiry?.ToLocalTime() < DateTime.Now)
                        {
                            EDDI.Instance.eventHandler(new MissionExpiredEvent(DateTime.Now, mission.missionid, mission.name));
                        }
                        else if (mission.expiry?.ToLocalTime() < DateTime.Now.AddMinutes(-missionWarning ?? -60))
                        {
                            EDDI.Instance.eventHandler(new MissionWarningEvent(DateTime.Now, mission.missionid, mission.name, span.Minutes));
                        }
                    }
                    else
                    {
                        mission.timeremaining = String.Empty;
                    }
                }
                Thread.Sleep(5000);
            }
        }

        public UserControl ConfigurationTabItem()
        {
            return new ConfigurationWindow();
        }

        public void EnableConfigBinding(MainWindow configWindow)
        {
            configWindow.Dispatcher.Invoke(() => { BindingOperations.EnableCollectionSynchronization(missions, missionsLock); });
        }

        public void DisableConfigBinding(MainWindow configWindow)
        {
            configWindow.Dispatcher.Invoke(() => { BindingOperations.DisableCollectionSynchronization(missions); });
        }

        public void HandleProfile(JObject profile)
        {
        }

        public void PostHandle(Event @event)
        {
            Logging.Debug("Received event " + JsonConvert.SerializeObject(@event));

            // 'Post' handle events which remove a mission from the log
            if (@event is MissionAbandonedEvent)
            {
                //
                handleMissionAbandonedEvent((MissionAbandonedEvent)@event);
            }
            else if (@event is MissionCompletedEvent)
            {
                //
                handleMissionCompletedEvent((MissionCompletedEvent)@event);
            }
            else if (@event is MissionFailedEvent)
            {
                //
                handleMissionFailedEvent((MissionFailedEvent)@event);
            }
        }

        public void PreHandle(Event @event)
        {
            Logging.Debug("Received event " + JsonConvert.SerializeObject(@event));

            // Handle the events that we care about
            if (@event is DataScannedEvent)
            {
                //
                handleDataScannedEvent((DataScannedEvent)@event);
            }
            else if (@event is PassengersEvent)
            {
                //
                handlePassengersEvent((PassengersEvent)@event);
            }
            else if (@event is MissionsEvent)
            {
                //
                handleMissionsEvent((MissionsEvent)@event);
            }
            else if (@event is CommunityGoalEvent)
            {
                //
                handleCommunityGoalEvent((CommunityGoalEvent)@event);
            }
            else if (@event is CargoDepotEvent)
            {
                //
                handleCargoDepotEvent((CargoDepotEvent)@event);
            }
            else if (@event is MissionAcceptedEvent)
            {
                //
                handleMissionAcceptedEvent((MissionAcceptedEvent)@event);
            }
            else if (@event is MissionExpiredEvent)
            {
                //
                handleMissionExpiredEvent((MissionExpiredEvent)@event);
            }
            else if (@event is MissionRedirectedEvent)
            {
                //
                handleMissionRedirectedEvent((MissionRedirectedEvent)@event);
            }
        }

        private void handleDataScannedEvent(DataScannedEvent @event)
        {
            _handleDataScannedEvent(@event);
            writeMissions();
        }

        private void _handleDataScannedEvent(DataScannedEvent @event)
        {
            string datalinktypeEDName = DataScan.FromName(@event.datalinktype).edname;
            if (datalinktypeEDName == "TouristBeacon")
            {
                bool handled = false;
                foreach (Mission mission in missions.ToList())
                {
                    string type = mission.typeEDName.ToLowerInvariant();
                    switch (type)
                    {
                        case "sightseeing":
                            {
                                DestinationSystem system = mission.destinationsystems
                                    .FirstOrDefault(s => s.name == EDDI.Instance?.CurrentStarSystem?.name);
                                if (system != null)
                                {
                                    system.visited = true;
                                    if (mission.destinationsystems.Where(s => s.visited == false).Count() > 0)
                                    {
                                        // Set destination system to next in chain & trigger a 'Mission redirected' event
                                        string destinationsystem = mission.destinationsystems
                                            .FirstOrDefault(s => s.visited == false).name;
                                        EDDI.Instance.eventHandler(new MissionRedirectedEvent(DateTime.Now, mission.missionid, mission.name, null, null, destinationsystem, EDDI.Instance?.CurrentStarSystem?.name));
                                    }
                                    handled = true;
                                }
                            }
                            break;
                    }
                    if (handled)
                    {
                        break;
                    }
                }
            }
        }

        private void handleMissionsEvent(MissionsEvent @event)
        {
            _handleMissionsEvent(@event);
            writeMissions();
        }

        public void _handleMissionsEvent(MissionsEvent @event)
        {
            foreach (Mission mission in @event.missions)
            {
                // Add missions to mission log
                Mission missionEntry = missions.FirstOrDefault(m => m.missionid == mission.missionid);
                if (missionEntry != null)
                {
                    if (missionEntry.statusDef == MissionStatus.FromEDName("Active"))
                    {
                        if (missionEntry.destinationsystem == missionEntry.originsystem)
                        {
                            switch (missionEntry.typeEDName)
                            {
                                case "assassinate":
                                case "disable":
                                case "longdistanceexpedition":
                                case "passengervip":
                                case "piracy":
                                case "rescue":
                                case "salvage":
                                case "scan":
                                case "sightseeing":
                                    {
                                        missionEntry.statusDef = MissionStatus.FromEDName("Complete");
                                    }
                                    break;
                            }
                        }
                        else
                        {
                            missionEntry.statusDef = mission.statusDef;
                        }
                    }

                    //If placeholder from 'Passengers' event, add 'Missions' parameters
                    if (missionEntry.name.Contains("None"))
                    {
                        missionEntry.name = mission.name;
                        missionEntry.typeDef = MissionType.FromEDName(mission.name.Split('_').ElementAt(1));
                        missionEntry.expiry = mission.expiry;
                    }
                }
                else
                {
                    AddMission(mission);
                }
            }
            // Remove strays from the mission log
            foreach (Mission missionEntry in missions.ToList())
            {
                Mission mission = @event.missions.FirstOrDefault(m => m.missionid == missionEntry.missionid);
                if (mission == null)
                {
                    // Strip out the stray from the mission log
                    _RemoveMissionWithMissionId(missionEntry.missionid);
                }
            }
        }

        private void handlePassengersEvent(PassengersEvent @event)
        {
            _handlePassengersEvent(@event);
            writeMissions();
        }

        private void _handlePassengersEvent(PassengersEvent @event)
        {
            Mission mission = new Mission();
            foreach (Passenger passenger in @event.passengers)
            {
                mission = missions.FirstOrDefault(m => m.missionid == passenger.missionid);
                if (mission != null)
                {
                    mission.passengertypeEDName = passenger.type;
                    mission.passengervips = passenger.vip;
                    mission.passengerwanted = passenger.wanted;
                    mission.amount = passenger.amount;
                }
                else
                {
                    // Dummy mission to populate 'Passengers' parameters
                    // 'Missions' event will populate 'name', 'status', 'type' & 'expiry'
                    MissionStatus status = MissionStatus.FromEDName("Active");
                    mission = new Mission(passenger.missionid, "Mission_None", DateTime.Now.AddDays(1), status)
                    {
                        passengertypeEDName = passenger.type,
                        passengervips = passenger.vip,
                        passengerwanted = passenger.wanted,
                        amount = passenger.amount
                    };
                    AddMission(mission);
                }

            }
        }

        private void handleCommunityGoalEvent(CommunityGoalEvent @event)
        {
            _handleCommunityGoalEvent(@event);
            writeMissions();
        }

        private void _handleCommunityGoalEvent(CommunityGoalEvent @event)
        {
            Mission mission = new Mission();
            for (int i = 0; i < @event.cgid.Count(); i++)
            {
                mission = missions.FirstOrDefault(m => m.missionid == @event.cgid[i]);
                if (mission == null)
                {
                    MissionStatus status = MissionStatus.FromEDName("Active");
                    mission = new Mission(@event.cgid[i], "MISSION_CommunityGoal", DateTime.Now.AddSeconds(@event.expiry[i]), status)
                    {
                        localisedname = @event.name[i],
                        originstation = @event.station[i]
                    };

                }
                else
                {
                    if (mission.expiry == null)
                    {
                        mission.expiry = DateTime.Now.AddSeconds(@event.expiry[i]);
                        mission.originstation = @event.station[i];
                    }
                }
            }
        }

            private void handleCargoDepotEvent(CargoDepotEvent @event)
        {
            _handleCargoDepotEvent(@event);
            writeMissions();
        }

        private void _handleCargoDepotEvent(CargoDepotEvent @event)
        {
            if (@event.missionid != null)
            {
                Mission mission = new Mission();
                int amountRemaining = @event.totaltodeliver - @event.delivered;
                switch (@event.updatetype)
                {
                    case "Collect":
                        {
                            mission = missions.FirstOrDefault(m => m.missionid == @event.missionid);
                            if (mission == null)
                            {
                                MissionStatus status = MissionStatus.FromEDName("Active");
                                mission = new Mission(@event.missionid ?? 0, "MISSION_DeliveryWing", null, status, true)
                                {
                                    commodity = @event.commodity,
                                    amount = @event.totaltodeliver,
                                    wing = true,
                                    originsystem = EDDI.Instance?.CurrentStarSystem?.name,
                                    originstation = EDDI.Instance?.CurrentStation?.name,
                                    originreturn = false
                                };

                                AddMission(mission);
                            }
                            else
                            {

                            }
                        }
                        break;
                    case "Deliver":
                    case "WingUpdate":
                        {
                            mission = missions.FirstOrDefault(m => m.missionid == @event.missionid);
                            if (mission == null)
                            {
                                if (amountRemaining > 0)
                                {
                                    MissionStatus status = MissionStatus.FromEDName("Active");
                                    string type = @event.collected == 0 ? "MISSION_CollectWing" : "MISSION_DeliveryWing";
                                    mission = new Mission(@event.missionid ?? 0, type, null, status, true)
                                    {
                                        amount = @event.totaltodeliver,
                                        commodity = @event.updatetype == "Deliver" ? @event.commodity : "Unknown",
                                        originsystem = @event.collected == 0 && @event.updatetype == "Deliver" ? EDDI.Instance?.CurrentStarSystem?.name : null,
                                        originstation = @event.collected == 0 && @event.updatetype == "Deliver" ? EDDI.Instance?.CurrentStarSystem?.name : null,
                                        wing = true,
                                        originreturn = @event.collected == 0 ? true : false
                                    };

                                    AddMission(mission);
                                }
                            }
                            else if (mission.shared)
                            {
                                if (amountRemaining > 0)
                                {
                                    if (@event.updatetype == "Deliver")
                                    {
                                        mission.commodity = @event.commodity;
                                        mission.originsystem = EDDI.Instance?.CurrentStarSystem?.name;
                                        mission.originstation = EDDI.Instance?.CurrentStation?.name;
                                    }
                                }
                                else
                                {
                                    RemoveMission(mission);
                                }
                            }
                        }
                        break;
                }
            }
        }

        private void handleMissionAbandonedEvent(MissionAbandonedEvent @event)
        {
            _handleMissionAbandonedEvent(@event);
            writeMissions();
        }

        public void _handleMissionAbandonedEvent(MissionAbandonedEvent @event)
        {
            if (@event.missionid != null)
            {
                Mission mission = missions.FirstOrDefault(m => m.missionid == @event.missionid);
                if (mission != null)
                {
                    _RemoveMissionWithMissionId(@event.missionid ?? 0);
                }
            }
        }

        private void handleMissionAcceptedEvent(MissionAcceptedEvent @event)
        {
            if (@event.name != null)
            {
                _handleMissionAcceptedEvent(@event);
                writeMissions();
            }
        }

        public void _handleMissionAcceptedEvent(MissionAcceptedEvent @event)
        {
            if (@event.missionid != null)
            {
                MissionStatus status = MissionStatus.FromEDName("Active");
                Mission mission = new Mission(@event.missionid ?? 0, @event.name, @event.expiry, status)
                {
                    // Common parameters
                    localisedname = @event.localisedname,
                    amount = @event.amount ?? 0,
                    influence = @event.influence,
                    reputation = @event.reputation,
                    reward = @event.reward ?? 0,
                    wing = @event.wing,
                    communal = @event.communal,

                    // Get the minor faction name
                    faction = @event.faction,

                    // Set mission origin to to the current system & station
                    originsystem = @event.communal ? @event.destinationsystem : EDDI.Instance?.CurrentStarSystem?.name,
                    originstation = @event.communal ? null : EDDI.Instance?.CurrentStation?.name,

                    // Missions with commodities
                    commodity = @event.commodity,

                    // Missions with targets
                    target = @event.target,
                    targetfaction = @event.targetfaction,

                    // Missions with passengers
                    passengertypeEDName = @event.passengertype,
                    passengervips = @event.passengervips,
                    passengerwanted = @event.passengerwanted
                };

                string type = mission.typeEDName.ToLowerInvariant();

                // Get the target type (Civilian, Pirate, Trader, etc), if available
                for (int i = 1; i < @event.targettype.Split('_').Count(); i++)
                {
                    TargetType targetType = TargetType.FromEDName(@event.targettype.Replace(";", "")
                        .Split('_')
                        .ElementAtOrDefault(i)?
                        .ToLowerInvariant());
                    if (targetType != null)
                    {
                        mission.targetTypeEDName = targetType.edname;
                        break;
                    }
                }

                    // Get the faction state (Boom, Bust, Civil War, etc), if available
                    for (int i = 2; i < mission.name.Split('_').Count(); i++)
                {
                    string factionState = SystemState.FromEDName(mission.name.Split('_')
                        .ElementAtOrDefault(i)?
                        .ToLowerInvariant())
                        .localizedName;
                    if (factionState != null)
                    {
                        mission.factionstate = factionState;
                        break;
                    }
                }

                // Mission returns to origin
                switch (type)
                {
                    case "altruism":
                    case "altruismcredits":
                    case "assassinate":
                    case "assassinatewing":
                    case "collect":
                    case "collectwing":
                    case "disable":
                    case "longdistanceexpedition":
                    case "massacre":
                    case "massacrewing":
                    case "mining":
                    case "piracy":
                    case "sightseeing":
                        {
                            mission.originreturn = true;
                        }
                        break;
                    default:
                        {
                            mission.originreturn = false;
                        }
                        break;
                }

                // Missions with multiple destinations
                if (@event.destinationsystem.Contains("$MISSIONUTIL_MULTIPLE"))
                {
                    // If 'chained' mission, get the destination systems
                    string[] systems = @event.destinationsystem
                        .Replace("$MISSIONUTIL_MULTIPLE_INNER_SEPARATOR;", "#")
                        .Replace("$MISSIONUTIL_MULTIPLE_FINAL_SEPARATOR;", "#")
                        .Split('#');

                    foreach (string system in systems)
                    {
                        mission.destinationsystems.Add(new DestinationSystem(system));
                    }

                    // Load the first destination system.
                    mission.destinationsystem = mission.destinationsystems.ElementAtOrDefault(0).name;
                }
                else
                {
                    // Populate destination system and station, depending on mission type
                    switch (type)
                    {
                        case "altruism":
                        case "altruismcredits":
                            {
                                mission.destinationsystem = mission.originsystem;
                                mission.destinationstation = mission.originstation;
                            }
                            break;
                        default:
                            {
                                mission.destinationsystem = @event.destinationsystem;
                                mission.destinationstation = @event.destinationstation;
                            }
                            break;
                    }
                }
                AddMission(mission);
            }
        }

        private void handleMissionCompletedEvent(MissionCompletedEvent @event)
        {

            _handleMissionCompletedEvent(@event);
            writeMissions();

        }

        public void _handleMissionCompletedEvent(MissionCompletedEvent @event)
        {
            if (@event.missionid != null)
            {
                Mission mission = missions.FirstOrDefault(m => m.missionid == @event.missionid);
                if (mission != null)
                {
                    _RemoveMissionWithMissionId(@event.missionid ?? 0);
                }
            }
        }

        private void handleMissionExpiredEvent(MissionExpiredEvent @event)
        {
            _handleMissionExpiredEvent(@event);
            writeMissions();
        }

        public void _handleMissionExpiredEvent(MissionExpiredEvent @event)
        {
            if (@event.missionid != null)
            {
                Mission mission = missions.FirstOrDefault(m => m.missionid == @event.missionid);
                if (mission != null)
                {
                    mission.statusDef = MissionStatus.FromEDName("Failed");
                }
            }
        }

        private void handleMissionFailedEvent(MissionFailedEvent @event)
        {
            _handleMissionFailedEvent(@event);
            writeMissions();
        }

        public void _handleMissionFailedEvent(MissionFailedEvent @event)
        {
            if (@event.missionid != null)
            {
                Mission mission = missions.FirstOrDefault(m => m.missionid == @event.missionid);
                if (mission != null)
                {
                    _RemoveMissionWithMissionId(@event.missionid ?? 0);
                }
            }
        }

        private void handleMissionRedirectedEvent(MissionRedirectedEvent @event)
        {
            _handleMissionRedirectedEvent(@event);
            writeMissions();
        }

        public void _handleMissionRedirectedEvent(MissionRedirectedEvent @event)
        {
            if (@event.missionid != null)
            {
                Mission mission = missions.FirstOrDefault(m => m.missionid == @event.missionid);
                if (mission != null)
                {
                    mission.destinationsystem = @event.newdestinationsystem;
                    mission.destinationstation = @event.newdestinationstation;

                    if (mission.originreturn && mission.originsystem == @event.newdestinationsystem
                        && mission.originstation == @event.newdestinationstation)
                    {
                        mission.statusDef = MissionStatus.FromEDName("Complete");
                    }
                }
            }
        }

        public IDictionary<string, object> GetVariables()
        {
            IDictionary<string, object> variables = new Dictionary<string, object>
            {
                ["missions"] = new List<Mission>(missions),
                ["missionsCount"] = missionsCount,
                ["missionWarning"] = missionWarning,
                ["missionsRouteList"] = missionsRouteList,
                ["missionsRouteDistance"] = missionsRouteDistance
            };
            return variables;
        }

        public void writeMissions()
        {
            lock (missionsLock)
            {
                // Write cargo configuration with current inventory
                MissionMonitorConfiguration configuration = new MissionMonitorConfiguration();

                configuration.missions = missions;
                missionsCount = missions.Where(m => !m.shared && !m.communal).Count();
                configuration.missionsCount = missionsCount;
                configuration.missionWarning = missionWarning;
                configuration.ToFile();
            }
            // Make sure the UI is up to date
            RaiseOnUIThread(MissionUpdatedEvent, missions);
        }

        private void readMissions(MissionMonitorConfiguration configuration = null)
        {
            lock (missionsLock)
            {
                // Obtain current missions inventory from configuration
                configuration = configuration ?? MissionMonitorConfiguration.FromFile();
                missionsCount = configuration.missionsCount;
                missionWarning = configuration.missionWarning ?? 60;

                // Build a new missions log
                List<Mission> newMissions = new List<Mission>();

                // Start with the missions we have in the log
                foreach (Mission mission in configuration.missions)
                {
                    newMissions.Add(mission);
                }

                // Now order the list by mission id
                newMissions = newMissions.OrderBy(m => m.missionid).ToList();

                // Update the missions log 
                missions.Clear();
                foreach (Mission mission in newMissions)
                {
                    missions.Add(mission);
                }
            }
        }

        private void AddMission(Mission mission)
        {
            if (mission == null)
            {
                return;
            }

            lock (missionsLock)
            {
                missions.Add(mission);
            }
            writeMissions();
        }

        private void RemoveMission(Mission mission)
        {
            _RemoveMissionWithMissionId(mission.missionid);
        }

        private void _RemoveMissionWithMissionId(long missionid)
        {
            lock (missionsLock)
            {
                for (int i = 0; i < missions.Count; i++)
                {
                    if (missions[i].missionid == missionid)
                    {
                        missions.RemoveAt(i);
                        break;
                    }
                }
            }
            writeMissions();
        }

        public bool BuildMissionsRoute(string homesystem)
        {
            missionsRouteList = String.Empty;
            missionsRouteDistance = 0;

            bool routeFound = false;
            StarSystem curr = new StarSystem();             // Current star system
            StarSystem dest = new StarSystem();             // Destination star system
            List<string> route = new List<string>();        // Proposed missions route
            List<string> systems = new List<string>();      // List of eligible mission destintaion systems

            // If 'home system' is null, default to the current star system
            string currentsystem = EDDI.Instance?.CurrentStarSystem?.name;
            if (homesystem == null)
            {
                homesystem = currentsystem;
            }
            systems.Add(homesystem);

            // Add origin systems for 'return to origin' missions to the 'systems' list
            foreach (Mission mission in missions.Where(m => m.statusEDName != "Failed").ToList())
            {
                if (mission.originreturn && !systems.Contains(mission.originsystem))
                {
                    systems.Add(mission.originsystem);
                }
            }

            // Add destination systems for applicable mission types to the 'systems' list
            foreach (Mission mission in missions.Where(m => m.statusEDName == "Active").ToList())
            {
                string type = mission.typeEDName.ToLowerInvariant();
                switch (type)
                {
                    case "assassinate":
                    case "courier":
                    case "delivery":
                    case "disable":
                    case "massacre":
                    case "passengerbulk":
                    case "passengervip":
                    case "rescue":
                    case "salvage":
                    case "scan":
                    case "sightseeing":
                    case "smuggle":
                        {
                            if (mission.destinationsystems == null)
                            {
                                if (!systems.Contains(mission.destinationsystem))
                                {
                                    systems.Add(mission.destinationsystem);
                                }
                            }
                            else
                            {
                                foreach (DestinationSystem system in mission.destinationsystems)
                                {
                                    if (!systems.Contains(system.name))
                                    {
                                        systems.Add(system.name);
                                    }
                                }
                            }
                        }
                        break;
                }
            }

            if (systems.Count() == 2)
            {
                route.Clear();
                route.Add(systems[1]);
                route.Add(systems[0]);
                missionsRouteList = string.Join("_", route);
                curr = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(systems[0], true);
                dest = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(systems[1], true);
                missionsRouteDistance = CalculateDistance(curr, dest) * 2;
                routeFound = true;
            }
            else if (systems.Count() > 2)
            {
                decimal distance;
                decimal nearestDistance;
                decimal totalDistance;
                string nearestSystem = String.Empty;

                // Repetitive Nearest Neighbor Algorithm (RNNA)
                // Iterate through all possible routes by changing the starting system
                for (int i = 0; i < systems.Count(); i++)
                {
                    // If starting system is a destination for a 'return to origin' mission, then not a viable route
                    if (DestinationOriginReturn(systems[i]))
                    {
                        break;
                    }

                    string startSystem = systems[i];
                    curr = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(startSystem, true);
                    route.Clear();
                    totalDistance = 0;

                    // Repeat until all systems (except starting system) are in the route
                    while (route.Count() < systems.Count() - 1)
                    {
                        nearestDistance = 0;

                        // Iterate through systems to find nearest neighbor
                        for (int j = 1; j < systems.Count(); j++)
                        {
                            // Wrap around the list
                            int index = i + j < systems.Count() ? i + j : i + j - systems.Count();

                            // Check systems not already added to the route
                            if (route.IndexOf(systems[index]) == -1)
                            {
                                dest = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(systems[index], true);
                                distance = CalculateDistance(curr, dest);

                                // Save if nearest to the 'current' system
                                if (nearestDistance == 0 || distance < nearestDistance)
                                {
                                    nearestDistance = distance;
                                    nearestSystem = systems[index];
                                }
                            }
                        }

                        // Add 'nearest' system to the route list and add its distance to total distance traveled
                        route.Add(nearestSystem);
                        totalDistance += nearestDistance;

                        // 'Nearest' system is the new 'current' system
                        curr = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(nearestSystem, true);
                    }

                    // Add 'starting system' to complete the route & add its distance to total distance traveled
                    route.Add(startSystem);
                    dest = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(startSystem, true);
                    totalDistance += CalculateDistance(curr, dest);
                    Logging.Debug("Build Route Iteration #" + i + " - Route = " + string.Join("_", route) + ", Total Distance = " + totalDistance);

                    // Use this route if total distance traveled is less then previous iterations
                    if (missionsRouteDistance == 0 || totalDistance < missionsRouteDistance)
                    {
                        missionsRouteDistance = totalDistance;
                        string temp = string.Join("_", route);
                        int index = missionsRouteList.IndexOf(homesystem) + homesystem.Length + 1;
                        if (index < temp.Length)
                        {
                            missionsRouteList = temp.Substring(index) + "_" + temp.Substring(0, index - 1);
                        }
                        routeFound = true;
                    }
                }
                Logging.Debug("Built Route Selected = " + missionsRouteList + ", Total Distance = " + missionsRouteDistance);
            }
            return routeFound;
        }

        public bool UpdateMissionsRoute(string updateSystem = null)
        {
            if (updateSystem == null)
            {
                string currentSystem = EDDI.Instance?.CurrentStarSystem?.name;
                updateSystem = missionsRouteList?.Split('_').ElementAtOrDefault(0);

                // Remove 'next' system from the missions route list, if current system & no pending missions
                if (currentSystem == updateSystem)
                {
                    foreach (Mission mission in missions.Where(m => m.typeEDName != "Fail").ToList())
                    {
                        // Check if 'next' system is origin system for 'Active' and 'Complete' missions
                        if (mission.originsystem == updateSystem)
                        {
                            return false;
                        }

                        // Check if 'next' system is destination system for 'Active' missions
                        if (mission.typeEDName == "Active")
                        {
                            if (mission.destinationsystems == null)
                            {
                                if (mission.destinationsystem == updateSystem)
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                foreach (DestinationSystem system in mission.destinationsystems)
                                {
                                    if (system.name == updateSystem)
                                    {
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    return false;
                }
            }
            else
            {
                // Remove 'update' system, regardless of mission status, if present in missions route list
                if (missionsRouteList.IndexOf(updateSystem) == -1)
                {
                    return false;
                }
            }

            // Remove 'update' system from the missions route list
            int index = missionsRouteList.IndexOf(updateSystem);
            int length = index + updateSystem.Length < missionsRouteList.Length ? updateSystem.Length + 1 : updateSystem.Length;
            missionsRouteList.Remove(index, length);
            return true;
        }

        private decimal CalculateDistance(StarSystem curr, StarSystem dest)
        {
            return (decimal)Math.Round(Math.Sqrt(Math.Pow((double)(curr.x - dest.x), 2)
                + Math.Pow((double)(curr.y - dest.y), 2)
                + Math.Pow((double)(curr.z - dest.z), 2)), 2);
        }

        private bool DestinationOriginReturn(string destination)
        {
            foreach (Mission mission in missions.Where(m => m.originreturn).ToList())
            {
                if (mission.destinationsystems == null)
                {
                    if (mission.destinationsystem == destination)
                    {
                        return true;
                    }
                }
                else
                {
                    DestinationSystem system = mission.destinationsystems.FirstOrDefault(ds => ds.name == destination);
                    if (system != null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public Mission GetMissionWithMissionId(long missionid)
        {
            return missions.FirstOrDefault(m => m.missionid == missionid);
        }

        static void RaiseOnUIThread(EventHandler handler, object sender)
        {
            if (handler != null)
            {
                SynchronizationContext uiSyncContext = SynchronizationContext.Current ?? new SynchronizationContext();
                if (uiSyncContext == null)
                {
                    handler(sender, EventArgs.Empty);
                }
                else
                {
                    uiSyncContext.Send(delegate { handler(sender, EventArgs.Empty); }, null);
                }
            }
        }
    }
}
