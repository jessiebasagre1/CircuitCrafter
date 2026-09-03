using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CircuitCrafter.Repair;

namespace CircuitCrafter.Repair.EditorTools
{
    /// <summary>
    /// Creates a library of COC4 repair tickets, fully written, from one menu
    /// click:
    ///
    ///     CircuitCrafter &gt; Generate COC4 Tickets
    ///
    /// PUT THIS FILE IN a folder named Editor.
    ///
    /// WHY GENERATE THEM
    /// A ticket is not much code but it is a lot of WRITING - a customer report,
    /// a technical symptom, and three or four probable causes each needing
    /// feedback that explains why it is right or wrong. That feedback is where
    /// the teaching actually happens, and typing it into the Inspector twelve
    /// times is where it quietly stops happening.
    ///
    /// EVERY TICKET HERE IS FIXABLE.
    /// Nothing is generated that the fault framework cannot actually inject and
    /// verify - no ticket describes a repair the learner has no way to perform.
    /// That is why there are no preventive-maintenance tickets yet: cleaning,
    /// thermal paste and backups need mechanics that do not exist, and a ticket
    /// that cannot be closed is worse than no ticket.
    ///
    /// SAFE TO RE-RUN: an existing ticketId anywhere in the project blocks
    /// creation, so your two hand-made tickets are never touched.
    ///
    /// AFTER GENERATING you must, per ticket: check Scene Name, add it to
    /// TicketManager's list, and put a matching TicketFault in the target scene
    /// with the same Active For Ticket Id. The Console prints the checklist.
    /// </summary>
    public static class TicketGenerator
    {
        // Edit these two if your repair-site scenes are named differently.
        private const string HardwareScene = "COC1-Copy";
        private const string NetworkScene = "COC3-Copy";

        private const string FolderParent = "Assets";
        private const string FolderName = "COC4Tickets";
        private const string FolderPath = FolderParent + "/" + FolderName;

        [MenuItem("CircuitCrafter/Generate COC4 Tickets")]
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(FolderPath))
                AssetDatabase.CreateFolder(FolderParent, FolderName);

            // Two INDEPENDENT passes, and that independence matters. The first
            // version created the observation set inside Create(), after the
            // "does this ticket already exist" check - so re-running it on a
            // project that already had the tickets created no observations at
            // all, and the Examine panel came up empty with nothing to explain
            // why. A missing observation set is now filled in whether or not its
            // ticket is new.
            int tickets = 0, observationSets = 0;
            foreach (var spec in BuildLibrary())
            {
                tickets += Create(spec) ? 1 : 0;
                observationSets += CreateObservations(spec) ? 1 : 0;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TicketGenerator] Created {tickets} ticket(s) and {observationSets} observation set(s) in {FolderPath}.\n\n" +
                      "FOR EACH NEW TICKET:\n" +
                      "  1. Check its Scene Name matches a real scene in Build Settings.\n" +
                      "  2. Add it to the TicketManager's All Tickets list in the COC4 hub.\n" +
                      "  3. Put a TicketFault in that scene with Active For Ticket Id set to the ticket's id.\n" +
                      "  4. Drag its *_Observations asset into the Site Observation Panel's Observation Sets list.\n\n" +
                      "A ticket with no matching fault loads a healthy machine and can never be closed - " +
                      "TicketFaultCoordinator warns when that happens.");
        }

        // ==================================================================
        // The library
        // ==================================================================

        private class Spec
        {
            public string id, title, description, symptom, scene;
            public TicketTargetModule module;
            public string faultNote;                       // printed as a reminder
            public readonly List<Option> options = new List<Option>();
            public readonly List<Ob> obs = new List<Ob>();
        }

        private class Ob
        {
            public string id, action, result;
            public int secs;
        }

        private class Option
        {
            public string label;
            public bool correct;
            public string feedback;
        }

        private static List<Spec> BuildLibrary()
        {
            var list = new List<Spec>();

            // ---------- hardware, at the COC1 repair site ----------

            var gpu = New(list, "ticket_coc1_gpu_missing", TicketTargetModule.COC1, HardwareScene,
                "No display after the unit was moved",
                "The PC was carried to another room. It powers on normally - fans and lights are fine - but the monitor stays black. The same monitor worked before the move.",
                "System powers on. Fans and drive activity normal. No POST beep. No signal on the monitor connected to the graphics card port.",
                "PartRemovedFault - remove the GPU, watch the GPU socket.");
            Opt(gpu, "Graphics card not properly seated", true,
                "Correct. Transporting a unit is a classic cause of an expansion card working loose. Reseat it fully and check the retention clip.");
            Opt(gpu, "Faulty monitor or video cable", false,
                "Worth ruling out and cheap to test - but the customer says the same monitor worked before the move, which points inside the case.");
            Opt(gpu, "Failed power supply", false,
                "The fans and lights are running, so the PSU is delivering power.");
            Opt(gpu, "Corrupted operating system", false,
                "An OS fault still shows POST and a boot screen first. A completely black display means the problem is earlier than the OS.");


            Obs(gpu, "power_on", "Press the power button and listen",
                "Fans spin up straight away and the drive light flickers. No beep of any kind.", 10);
            Obs(gpu, "check_screen", "Look at the monitor",
                "Completely black. No manufacturer logo, no error text. The monitor's own light is on and it reports \"No signal\".", 10);
            Obs(gpu, "check_cable", "Check the video cable",
                "Firmly connected at both ends, into the port on the expansion card rather than the motherboard.", 15);
            Obs(gpu, "open_case", "Open the case and inspect",
                "No burning smell and nothing obviously disconnected. The graphics card is sitting slightly proud of its slot, and the retention clip at the end of the slot is open.", 30);

            var ssd = New(list, "ticket_coc1_ssd_missing", TicketTargetModule.COC1, HardwareScene,
                "\"No boot device found\"",
                "The machine starts up but shows an error about no operating system. The customer says nothing was changed.",
                "System powers on and completes POST normally. On screen: \"No boot device found. Press any key to reboot.\"",
                "PartRemovedFault - remove the SSD, watch the SSD bay socket.");
            Opt(ssd, "Storage drive not detected or not seated", true,
                "Correct. POST completed, so the board and memory are fine - the machine simply cannot find a drive to boot from.");
            Opt(ssd, "Memory failure", false,
                "POST completed and the message displayed, so memory is working. A RAM fault stops the machine before this point.");
            Opt(ssd, "Monitor fault", false,
                "The error message is visible on that monitor, so the display is fine.");
            Opt(ssd, "Operating system needs reinstalling", false,
                "Possible eventually - but check the drive is present and connected before erasing anything. Reinstalling onto a drive the machine cannot see will not work.");


            Obs(ssd, "power_on", "Press the power button and listen",
                "Fans spin. One short beep - the normal sound for a system that has passed POST.", 10);
            Obs(ssd, "check_screen", "Look at the monitor",
                "The board's logo appears normally, then: \"No boot device found. Press any key to reboot.\"", 10);
            Obs(ssd, "check_setup", "Enter setup and look at the drive list",
                "The storage list is empty. No drive is detected on any port.", 25);
            Obs(ssd, "open_case", "Open the case and inspect",
                "Everything is tidy and dust-free. The drive bay is empty - no storage drive is fitted.", 30);

            var cpu = New(list, "ticket_coc1_cpu_unseated", TicketTargetModule.COC1, HardwareScene,
                "Nothing happens after internal cleaning",
                "The unit was opened for cleaning. Since then it does almost nothing - the fans twitch and it stops. No picture at all.",
                "Fans spin briefly then settle. No POST, no beep code, no display output from any port.",
                "PartRemovedFault - remove the CPU, watch the CPU socket.");
            Opt(cpu, "Processor not correctly seated after cleaning", true,
                "Correct. With no working processor the board cannot run POST at all - which is why there is no beep code, not even an error one.");
            Opt(cpu, "Memory not seated", false,
                "A memory fault produces a POST beep code, because the processor is running well enough to report it. Here there is no beep at all.");
            Opt(cpu, "Failed power supply", false,
                "The fans move, so power is reaching the board.");
            Opt(cpu, "Graphics card not seated", false,
                "A missing graphics card still allows POST and a beep code. Complete silence points further back.");


            Obs(cpu, "power_on", "Press the power button and listen",
                "Fans move for about a second, then settle to a slow spin. No beep at all - not even an error one.", 10);
            Obs(cpu, "check_screen", "Look at the monitor",
                "Nothing. The monitor never comes out of standby.", 10);
            Obs(cpu, "check_psu", "Check the power supply and its connectors",
                "The PSU fan runs and the board's standby light is lit. Both power connectors are firmly seated.", 20);
            Obs(cpu, "open_case", "Open the case and inspect",
                "The cooler has been taken off and set aside from the cleaning. The processor socket is empty and its retention lever is standing up.", 30);

            var multi = New(list, "ticket_coc1_transport_damage", TicketTargetModule.COC1, HardwareScene,
                "Unit arrived from another branch and won't start",
                "This machine was shipped between offices. It was working when it was packed. Now it powers on but shows nothing on screen.",
                "System powers on, fans normal. No display. POST beep code: 1 long, 3 short. Case screws were found loose.",
                "TWO faults - PartRemovedFault (RAM) and PartRemovedFault (GPU), both scoped to this ticket. The coordinator registers a 2-step task automatically.");
            Opt(multi, "More than one component has worked loose in transit", true,
                "Correct. The beep code points at memory, but loose case screws and a shipped unit mean you should check every seated component before closing the job - fixing only the first fault you find leaves the customer with a machine that still does not work.");
            Opt(multi, "Memory not seated", false,
                "The beep code does say memory, and that is part of it - but reseating only the RAM leaves this machine still faulty. Look wider on a unit that has been shipped.");
            Opt(multi, "Failed motherboard", false,
                "A dead board would not produce a valid beep code. The board is running well enough to tell you what is wrong.");
            Opt(multi, "Power supply failure", false,
                "Fans and POST beeps both need power, and both are happening.");

            // ---------- network, at the COC3 repair site ----------


            Obs(multi, "power_on", "Press the power button and listen",
                "Fans spin normally. Beep code: one long, three short, repeating.", 10);
            Obs(multi, "check_screen", "Look at the monitor",
                "Black. No logo, no error message.", 10);
            Obs(multi, "check_packaging", "Look over the case and the packaging",
                "Two case screws are loose in the bottom of the box. The side panel was not fully secured.", 15);
            Obs(multi, "open_case", "Open the case and inspect",
                "One memory module is raised at one end with its clip open. The graphics card is also not fully home in its slot.", 30);

            var mask = New(list, "ticket_coc3_wrong_mask", TicketTargetModule.COC3, NetworkScene,
                "Workstation cannot reach the server",
                "This workstation cannot open the shared folder any more. It worked yesterday. Another technician was \"tidying up the network settings\" that afternoon.",
                "The workstation has an IP address and the cable is connected. Pinging the server returns: Reply from 192.168.1.20: Destination host unreachable.",
                "NetworkFault - kind Wrong Subnet Mask, ping target = the server.");
            Opt(mask, "Incorrect subnet mask", true,
                "Correct. With the wrong mask the machine believes the server is on a different network, so it tries to route to a gateway instead of speaking to it directly - and reports the destination as unreachable.");
            Opt(mask, "Network cable unplugged", false,
                "ipconfig would report \"Media disconnected\" and there would be no address at all. The ping also failed at this machine rather than timing out, but it did leave the adapter.");
            Opt(mask, "Server is switched off", false,
                "A server that is off gives \"Request timed out\" - the packet leaves and nothing answers. \"Destination host unreachable\" means this machine could not even work out where to send it.");
            Opt(mask, "Duplicate IP address", false,
                "An address conflict disables the interface completely, so you would see a transmit failure rather than this message.");


            Obs(mask, "check_link", "Check the network cable and link light",
                "Cable is seated at both ends. The link light is on and flickering normally.", 10);
            Obs(mask, "run_ipconfig", "Run ipconfig on the workstation",
                "IPv4 Address 192.168.1.20, Subnet Mask 255.255.255.240, Default Gateway 192.168.1.1.", 15);
            Obs(mask, "ping_server", "Ping the server",
                "Reply from 192.168.1.20: Destination host unreachable.", 15);
            Obs(mask, "ask_user", "Ask the user what changed",
                "\"Another technician was in yesterday afternoon. He said he was tidying up the network settings.\"", 10);

            var dup = New(list, "ticket_coc3_duplicate_ip", TicketTargetModule.COC3, NetworkScene,
                "New PC knocked the office offline",
                "A replacement PC was set up this morning. Since then that machine has no network at all, and staff say another computer keeps dropping out too.",
                "The workstation's IP settings look correct and match the documentation. No network traffic works. Windows has reported an IP address conflict.",
                "NetworkFault - kind Duplicate Address, ping target = the server (it copies the target's address).");
            Opt(dup, "Two devices are using the same IP address", true,
                "Correct. Everything looks right on both machines, which is what makes this one hard - the settings are only wrong in relation to each other. Give one of them a different address.");
            Opt(dup, "Faulty network card in the new PC", false,
                "A dead card would not produce a conflict warning - Windows can only report a conflict because the card is working and talking.");
            Opt(dup, "Incorrect default gateway", false,
                "A wrong gateway breaks traffic to other networks but leaves the local network working. Here nothing works at all.");
            Opt(dup, "The switch has failed", false,
                "A failed switch would take out far more than two machines, and would not cause a conflict warning.");


            Obs(dup, "check_link", "Check the network cable and link light",
                "Cable seated, link light on and flickering.", 10);
            Obs(dup, "run_ipconfig", "Run ipconfig on the workstation",
                "IPv4 Address 192.168.1.10, Subnet Mask 255.255.255.0, Default Gateway 192.168.1.1 - which matches the documentation exactly.", 15);
            Obs(dup, "check_notifications", "Check Windows notifications",
                "\"Windows has detected an IP address conflict. Another computer on this network has the same IP address as this computer.\"", 15);
            Obs(dup, "ping_server", "Ping the server",
                "PING: transmit failed. General failure.", 15);

            var link = New(list, "ticket_coc3_link_down", TicketTargetModule.COC3, NetworkScene,
                "No network on one workstation",
                "One machine in the lab has no network. The user says a cleaner was working around the desks yesterday evening.",
                "ipconfig reports: Media State . . . : Media disconnected. No IPv4 address is listed. The link light on the network port is off.",
                "NetworkFault - kind Cable Unplugged, ping target = the server.");
            Opt(link, "Network cable disconnected or faulty", true,
                "Correct. \"Media disconnected\" means the adapter cannot see a link at all - that is a physical problem, and no amount of changing IP settings will fix it. Check the cable at both ends first.");
            Opt(link, "Incorrect IP configuration", false,
                "There is no address to be wrong. The adapter reports no link, which happens before addressing is even considered.");
            Opt(link, "DHCP server not responding", false,
                "That gives a 169.254 self-assigned address, not \"Media disconnected\". The machine has no link to ask over.");
            Opt(link, "Firewall blocking traffic", false,
                "A firewall filters traffic on a working connection. This adapter has no connection to filter.");


            Obs(link, "check_link", "Check the network cable and link light",
                "The link light on the network port is off. The cable is in the port but does not click when pushed.", 10);
            Obs(link, "run_ipconfig", "Run ipconfig on the workstation",
                "Media State . . . : Media disconnected. No IPv4 address is listed.", 15);
            Obs(link, "ping_server", "Ping the server",
                "PING: transmit failed. General failure.", 15);

            var gw = New(list, "ticket_coc3_wrong_gateway", TicketTargetModule.COC3, NetworkScene,
                "Can print, but cannot reach the internet",
                "This machine can still open the shared drive and print, but nothing outside the office works.",
                "Pings to other machines on the same network succeed. Pings to anything on another network return: Destination host unreachable.",
                "NetworkFault - kind Wrong Gateway, ping target = a host on another subnet.");
            Opt(gw, "Default gateway is wrong or not on this subnet", true,
                "Correct. Local traffic never needs the gateway, which is why the shared drive still works - the split between what works and what does not is the clue.");
            Opt(gw, "Network cable is faulty", false,
                "Local traffic is working, so the cable and the link are fine.");
            Opt(gw, "Wrong IP address on this machine", false,
                "A wrong address would break local traffic too. Printing and the shared drive both work.");
            Opt(gw, "The server is down", false,
                "The server is answering - the shared drive and printer are on it.");


            Obs(gw, "ping_local", "Ping another workstation on this floor",
                "Reply from 192.168.1.31: bytes=32 time<1ms TTL=128. Four replies, no loss.", 15);
            Obs(gw, "ping_remote", "Ping a machine at head office",
                "Reply from 192.168.1.20: Destination host unreachable.", 15);
            Obs(gw, "run_ipconfig", "Run ipconfig on the workstation",
                "IPv4 Address 192.168.1.20, Subnet Mask 255.255.255.0, Default Gateway 192.168.9.1.", 15);

            var apipa = New(list, "ticket_coc3_no_dhcp", TicketTargetModule.COC3, NetworkScene,
                "Machine gave itself a strange address",
                "A workstation that normally picks up its settings automatically has no network this morning. Nothing else in the room is affected.",
                "ipconfig shows IPv4 Address 169.254.14.62 with subnet mask 255.255.0.0 and no default gateway. The cable is connected and the link light is on.",
                "NetworkFault - kind Dhcp With No Server, ping target = the server.");
            Opt(apipa, "No DHCP server responded, so Windows self-assigned an address", true,
                "Correct. 169.254.x.x is the automatic private range Windows falls back to when nobody answers its request. Either fix the DHCP service or set a static address that fits the network.");
            Opt(apipa, "Someone typed the wrong IP address in by hand", false,
                "A person would be unlikely to invent a 169.254 address - that range is Windows telling you it gave up waiting. Check whether the adapter is set to obtain an address automatically.");
            Opt(apipa, "Network cable unplugged", false,
                "The link light is on and ipconfig shows an address rather than \"Media disconnected\".");
            Opt(apipa, "Faulty network card", false,
                "The card is working well enough to request an address and self-assign one when nothing replied.");


            Obs(apipa, "check_link", "Check the network cable and link light",
                "Cable seated, link light on and flickering.", 10);
            Obs(apipa, "run_ipconfig", "Run ipconfig on the workstation",
                "IPv4 Address 169.254.14.62, Subnet Mask 255.255.0.0, and no default gateway listed.", 15);
            Obs(apipa, "check_adapter", "Open the adapter's IPv4 properties",
                "Set to \"Obtain an IP address automatically\". Nothing has been entered by hand.", 15);
            Obs(apipa, "ping_server", "Ping the server",
                "PING: transmit failed. General failure.", 15);

            return list;
        }

        // ==================================================================

        private static Spec New(List<Spec> list, string id, TicketTargetModule module, string scene,
                                string title, string description, string symptom, string faultNote)
        {
            var spec = new Spec
            {
                id = id,
                module = module,
                scene = scene,
                title = title,
                description = description,
                symptom = symptom,
                faultNote = faultNote
            };
            list.Add(spec);
            return spec;
        }

        private static void Opt(Spec spec, string label, bool correct, string feedback)
        {
            spec.options.Add(new Option { label = label, correct = correct, feedback = feedback });
        }

        /// <summary>Adds one thing the learner can check. Write the result as an
        /// OBSERVATION, never a conclusion - "no beep at all", not "the CPU is
        /// missing". The cheap checks should narrow it; opening the case should
        /// confirm it and cost the most.</summary>
        private static void Obs(Spec spec, string id, string action, string result, int secs)
        {
            spec.obs.Add(new Ob { id = id, action = action, result = result, secs = secs });
        }

        private static bool Create(Spec spec)
        {
            string existing = FindExisting(spec.id);
            if (existing != null)
            {
                Debug.Log($"[TicketGenerator] '{spec.id}' already exists at {existing} - left untouched.");
                return false;
            }

            var ticket = ScriptableObject.CreateInstance<TicketData>();
            ticket.ticketId = spec.id;
            ticket.targetModule = spec.module;
            ticket.sceneName = spec.scene;
            ticket.title = spec.title;
            ticket.description = spec.description;
            ticket.symptomText = spec.symptom;
            ticket.requiresDiagnosis = true;

            // Same string as the ticket id. It keeps the two impossible to get out
            // of step, and the fault framework reads it from RepairTicketMode
            // rather than hardcoding it anywhere.
            ticket.requiredTaskId = spec.id;

            var options = new List<TicketData.DiagnosisOption>();
            foreach (var option in spec.options)
            {
                options.Add(new TicketData.DiagnosisOption
                {
                    label = option.label,
                    isCorrect = option.correct,
                    feedback = option.feedback
                });
            }
            ticket.diagnosisOptions = options.ToArray();

            AssetDatabase.CreateAsset(ticket, $"{FolderPath}/{ToAssetName(spec.id)}.asset");
            EditorUtility.SetDirty(ticket);

            Debug.Log($"[TicketGenerator] Created '{spec.id}' — needs: {spec.faultNote}");
            return true;
        }

        /// <summary>The matching TicketObservationSet - what the learner can check
        /// at the site. Written here rather than on TicketData so nothing in the
        /// existing ticket asset has to change.</summary>
        private static bool CreateObservations(Spec spec)
        {
            if (spec.obs.Count == 0) return false;

            string existing = FindExistingObservations(spec.id);
            if (existing != null)
            {
                Debug.Log($"[TicketGenerator] Observations for '{spec.id}' already exist at {existing} - left untouched.");
                return false;
            }

            var set = ScriptableObject.CreateInstance<TicketObservationSet>();
            set.ticketId = spec.id;

            var list = new List<TicketObservationSet.Observation>();
            foreach (var ob in spec.obs)
            {
                list.Add(new TicketObservationSet.Observation
                {
                    observationId = ob.id,
                    action = ob.action,
                    result = ob.result,
                    timeCostSeconds = ob.secs
                });
            }
            set.observations = list.ToArray();

            AssetDatabase.CreateAsset(set, $"{FolderPath}/{ToAssetName(spec.id)}_Observations.asset");
            EditorUtility.SetDirty(set);
            return true;
        }

        private static string FindExistingObservations(string ticketId)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:TicketObservationSet"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var existing = AssetDatabase.LoadAssetAtPath<TicketObservationSet>(path);
                if (existing != null && existing.ticketId == ticketId) return path;
            }
            return null;
        }

        /// <summary>Searches the whole project by ticketId, not by filename - the
        /// lesson from the competency assets, where checking only one path let
        /// duplicates pile up in other folders.</summary>
        private static string FindExisting(string ticketId)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:TicketData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var existing = AssetDatabase.LoadAssetAtPath<TicketData>(path);
                if (existing != null && existing.ticketId == ticketId) return path;
            }
            return null;
        }

        private static string ToAssetName(string ticketId)
        {
            // ticket_coc1_gpu_missing -> Ticket_Coc1_Gpu_Missing
            string[] parts = ticketId.Split('_');
            var sb = new System.Text.StringBuilder();
            foreach (string part in parts)
            {
                if (part.Length == 0) continue;
                if (sb.Length > 0) sb.Append('_');
                sb.Append(char.ToUpperInvariant(part[0])).Append(part.Substring(1));
            }
            return sb.ToString();
        }
    }
}