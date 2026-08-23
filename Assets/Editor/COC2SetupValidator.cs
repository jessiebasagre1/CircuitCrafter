using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// A one-shot wiring check for the whole COC2 station. Runs once at scene start, walks every
    /// COC2 component it can find, and prints ONE consolidated report instead of letting each
    /// script warn separately - and repeatedly - as the player stumbles into the missing piece.
    ///
    /// Why this exists: most COC2 failures are an empty Inspector field, not a bug, and the
    /// symptom usually shows up somewhere unrelated. "endOne or endTwo isn't assigned" only
    /// appears once the player has already stripped the cable and started placing wires, and it
    /// then repeats on every single wire. This tells you at frame one instead.
    ///
    /// It only ever READS - it never assigns anything for you. Nothing here changes behaviour.
    ///
    /// Setup:
    ///   1. Drop this on any always-active object in the COC2 scene.
    ///   2. Press Play once and read the Console.
    ///   3. Leave it in - it costs one frame at startup and it'll catch the next broken reference
    ///      too. Or untick it once the scene is stable.
    /// </summary>
    public class COC2SetupValidator : MonoBehaviour
    {
        [Header("What to check")]
        [Tooltip("Untick if you haven't built the tester yet (Part B) and don't want to see those lines.")]
        public bool checkTestingStage = true;
        [Tooltip("Untick if this scene deliberately has no TaskManager.")]
        public bool checkTasks = true;
        [Tooltip("Name of the physics layer the cable plugs live on, checked against WireDragController's " +
                 "wire mask. Clear this to skip that check.")]
        public string cablePlugLayerName = "CablePlug";

        readonly List<string> problems = new List<string>();
        readonly List<string> notes = new List<string>();

        void Start()
        {
            problems.Clear();
            notes.Clear();

            CheckStation();
            CheckCrimpingView();
            CheckStrippingStage();
            CheckCrimpStage();
            CheckSupportingUI();
            if (checkTestingStage) CheckTestingStage();
            CheckLayers();
            if (checkTasks) CheckTasks();

            Report();
        }

        // ------------------------------------------------------------------

        void CheckStation()
        {
            var stations = FindAll<CrimpingStationController>();
            if (stations.Length == 0)
            {
                problems.Add("No CrimpingStationController in the scene. Nothing else in COC2 can work without it.");
                return;
            }
            if (stations.Length > 1)
                problems.Add($"{stations.Length} CrimpingStationControllers found. Only the last one to Awake() " +
                             "becomes .Instance, so the others are silently ignored - delete the extras.");

            var station = stations[0];
            if (station.endOne == null) problems.Add("CrimpingStationController > End One is EMPTY. Assign the ConnectorEnd on your End 1 housing. Until you do, End 1 can never be crimped.");
            if (station.endTwo == null) problems.Add("CrimpingStationController > End Two is EMPTY. Assign the ConnectorEnd on your End 2 housing. Until you do, End 2 can never be crimped.");
            if (station.targetCableType == null) problems.Add("CrimpingStationController > Target Cable Type is EMPTY. Assign a CableTypeSO, or make sure CableTypeSelectionUI sets one before the player reaches the wires.");

            if (station.endOne != null && station.endTwo != null && station.endOne == station.endTwo)
                problems.Add("CrimpingStationController > End One and End Two point at the SAME ConnectorEnd.");

            if (station.endOne != null && station.endOne.endNumber != 1)
                problems.Add($"The ConnectorEnd in End One has endNumber {station.endOne.endNumber}, expected 1.");
            if (station.endTwo != null && station.endTwo.endNumber != 2)
                problems.Add($"The ConnectorEnd in End Two has endNumber {station.endTwo.endNumber}, expected 2.");

            CheckSlots(station.endOne, 1);
            CheckSlots(station.endTwo, 2);

            if (station.autoCheckMode == CrimpingStationController.AutoCheckMode.ReturnWiresWhenWrong)
                notes.Add("Auto Check Mode is ReturnWiresWhenWrong, so a wrong arrangement can never be crimped - " +
                          "the cable tester will always pass. Set it to FeedbackOnly if you want faults to reach the tester.");
        }

        void CheckSlots(ConnectorEnd end, int expectedEndNumber)
        {
            if (end == null) return;

            if (end.slots == null || end.slots.Length != 8)
            {
                problems.Add($"ConnectorEnd '{end.name}' has {(end.slots == null ? 0 : end.slots.Length)} slots, expected 8.");
                return;
            }

            var seenPins = new HashSet<int>();
            for (int i = 0; i < 8; i++)
            {
                var slot = end.slots[i];
                if (slot == null) { problems.Add($"ConnectorEnd '{end.name}' > Slots element {i} is EMPTY."); continue; }
                if (slot.endNumber != expectedEndNumber)
                    problems.Add($"WirePinSlot '{slot.name}' has endNumber {slot.endNumber} but sits under End {expectedEndNumber}.");
                if (!seenPins.Add(slot.pinNumber))
                    problems.Add($"ConnectorEnd '{end.name}' has two slots both set to pin {slot.pinNumber}.");
            }
            for (int pin = 1; pin <= 8; pin++)
            {
                if (!seenPins.Contains(pin))
                    problems.Add($"ConnectorEnd '{end.name}' has no slot for pin {pin}.");
            }
        }

        void CheckCrimpingView()
        {
            var view = FindOne<CrimpingViewController>();
            if (view == null) { problems.Add("No CrimpingViewController in the scene."); return; }

            if (view.playerCamera == null) problems.Add("CrimpingViewController > Player Camera is EMPTY.");
            if (view.crimpingCamera == null) problems.Add("CrimpingViewController > Crimping Camera is EMPTY.");
            if (view.crimpingToolVisual == null) problems.Add("CrimpingViewController > Crimping Tool Visual is EMPTY.");
            if (view.crimpToolDraggable == null)
                problems.Add("CrimpingViewController > Crimp Tool Draggable is EMPTY. The player will never be able " +
                             "to drag the tool onto a connector, so crimping can't start.");
            else if (view.crimpToolDraggable.dragCamera == null && view.crimpingCamera == null)
                problems.Add("CrimpToolCrimpDraggable > Drag Camera is EMPTY and there's no crimping camera to fall back on.");

            if (view.noToolDialog == null)
                notes.Add("CrimpingViewController > No Tool Dialog is empty - the 'you need the crimping tool' " +
                          "warning will only appear in the Console.");
        }

        void CheckStrippingStage()
        {
            var strip = FindOne<StrippingViewController>();
            if (strip == null) { problems.Add("No StrippingViewController in the scene."); return; }

            if (strip.strippingCamera == null) problems.Add("StrippingViewController > Stripping Camera is EMPTY.");
            if (strip.crimpingToolVisual == null) problems.Add("StrippingViewController > Crimping Tool Visual is EMPTY.");
            if (strip.toolDraggable == null) problems.Add("StrippingViewController > Tool Draggable is EMPTY.");

            if (FindOne<CrimpToolStripController>() == null)
                problems.Add("No CrimpToolStripController in the scene - stripping can't hand off to crimping.");
        }

        void CheckCrimpStage()
        {
            var crimp = FindOne<CrimpToolCrimpController>();
            if (crimp == null)
            {
                problems.Add("No CrimpToolCrimpController in the scene. Add it to your crimp animation prefab's root.");
                return;
            }

            var ends = crimp.Ends;
            if (ends == null || ends.Length < 2)
            {
                problems.Add("CrimpToolCrimpController > Ends needs 2 entries, one per connector end.");
                return;
            }

            var seen = new HashSet<int>();
            foreach (var end in ends)
            {
                if (end == null) { problems.Add("CrimpToolCrimpController > Ends has an empty element."); continue; }
                if (!seen.Add(end.endNumber))
                    problems.Add($"CrimpToolCrimpController > two Ends entries both use End Number {end.endNumber}.");
                if (end.endNumber != 1 && end.endNumber != 2)
                    problems.Add($"CrimpToolCrimpController > Ends has End Number {end.endNumber}; only 1 and 2 are valid.");
                if (end.toolDropAnchor == null)
                    problems.Add($"CrimpToolCrimpController > Ends (End {end.endNumber}) > Tool Drop Anchor is EMPTY. " +
                                 "That end has no drop zone, so the tool can never be dropped on it.");
                if (end.realConnectorVisual == null)
                    notes.Add($"CrimpToolCrimpController > Ends (End {end.endNumber}) > Real Connector Visual is empty - " +
                              "the player's connector will overlap the rig's baked one during the clip.");
            }

            // Two anchors closer together than the draggable's drop radius means a drop meant for one
            // end can trigger the other.
            var draggable = FindOne<CrimpToolCrimpDraggable>();
            if (draggable != null && ends.Length >= 2 &&
                ends[0] != null && ends[1] != null &&
                ends[0].toolDropAnchor != null && ends[1].toolDropAnchor != null)
            {
                float gap = Vector3.Distance(ends[0].toolDropAnchor.position, ends[1].toolDropAnchor.position);
                if (draggable.dropDistance >= gap * 0.5f)
                    problems.Add($"CrimpToolCrimpDraggable > Drop Distance ({draggable.dropDistance:0.###}) is at least half " +
                                 $"the gap between the two crimp anchors ({gap:0.###}). A drop near one end can trigger the " +
                                 $"other - set it below {gap * 0.5f:0.###}.");
            }

            if (draggable != null && draggable.enabled)
                notes.Add("CrimpToolCrimpDraggable is enabled at startup. CrimpingViewController turns it on itself, " +
                          "so it should normally start unticked - otherwise the tool is draggable during stripping too.");
        }

        void CheckSupportingUI()
        {
            var selection = FindOne<CableTypeSelectionUI>();
            if (selection != null)
            {
                if (selection.station == null)
                    problems.Add("CableTypeSelectionUI > Station is EMPTY. The player's cable-type choice is thrown away, " +
                                 "so the station keeps whatever Target Cable Type was set in the Inspector.");
                if (selection.panel == null)
                    problems.Add("CableTypeSelectionUI > Panel is EMPTY.");
                if (selection.straightThroughCableType == null || selection.crossoverCableType == null)
                    problems.Add("CableTypeSelectionUI > one of the two cable type assets is EMPTY.");
            }

            var guides = FindAll<CrimpingGuideUI>();
            foreach (var guide in guides)
            {
                if (guide.station == null)
                    problems.Add($"CrimpingGuideUI on '{guide.name}' > Station is EMPTY - that guide panel won't follow " +
                                 "the player's cable-type choice.");
            }
        }

        void CheckTestingStage()
        {
            if (FindOne<CableInventory>() == null)
                problems.Add("No CableInventory in the scene. Add it to the same Managers object that holds " +
                             "CrimpingToolInventory, or the crimped cable can never be picked up.");

            var pickup = FindOne<CrimpedCablePickup>();
            if (pickup == null)
            {
                problems.Add("No CrimpedCablePickup in the scene - the finished cable will never appear on the table.");
            }
            else
            {
                if (pickup.cableVisual == null)
                    problems.Add("CrimpedCablePickup > Cable Visual is EMPTY, so nothing appears on the table when " +
                                 "crimping finishes.");
                if (!pickup.gameObject.activeInHierarchy)
                    problems.Add("The CrimpedCablePickup GameObject is INACTIVE. It must stay active - it hides its " +
                                 "own visual and can't listen for the crimp finishing while switched off.");
                if (string.IsNullOrEmpty(pickup.promptText))
                    notes.Add("CrimpedCablePickup > Prompt Text is blank - the player gets no 'press E' hint.");
            }

            var tester = FindOne<NetworkTestStation>();
            if (tester == null)
            {
                problems.Add("No NetworkTestStation in the scene. (Untick Check Testing Stage on this validator " +
                             "if you haven't built the tester bench yet.)");
                return;
            }

            if (tester.playerCamera == null) problems.Add("NetworkTestStation > Player Camera is EMPTY.");
            if (tester.testerCamera == null) problems.Add("NetworkTestStation > Tester Camera is EMPTY.");
            if (tester.testerCamera != null && tester.testerCamera == tester.playerCamera)
                problems.Add("NetworkTestStation > Player Camera and Tester Camera are the SAME camera.");

            if (tester.stationCableVisual == null)
                problems.Add("NetworkTestStation > Station Cable Visual is EMPTY. Nothing will appear at the tester, " +
                             "and with Click Cable To Plug ticked there's nothing to click either.");
            else if (tester.stationCableVisual.GetComponentsInChildren<Renderer>(true).Length == 0)
                problems.Add("NetworkTestStation > Station Cable Visual has no Renderers, so clicking it can never " +
                             "register. Point it at the cable MODEL, not an empty parent.");

            if (tester.testerAnimator == null)
                notes.Add("NetworkTestStation > Tester Animator is empty - the test still resolves, it just plays " +
                          "no animation.");

            if (string.IsNullOrEmpty(tester.passTrigger) || string.IsNullOrEmpty(tester.failTrigger))
                notes.Add("NetworkTestStation > one of the pass/fail trigger names is blank.");

            if (!tester.clickCableToPlug)
                notes.Add("NetworkTestStation > Click Cable To Plug is off, so a UI button must be wired to " +
                          "PlugIn() or the test can never start.");

            if (tester.playerControlScripts == null || tester.playerControlScripts.Length == 0)
                notes.Add("NetworkTestStation > Player Control Scripts is empty - the player will still be able to " +
                          "walk around while the tester close-up is open.");
        }

        void CheckLayers()
        {
            if (string.IsNullOrEmpty(cablePlugLayerName)) return;

            int layer = LayerMask.NameToLayer(cablePlugLayerName);
            if (layer < 0)
            {
                if (checkTestingStage)
                    notes.Add($"No physics layer named '{cablePlugLayerName}' exists yet. You'll need it before the " +
                              "cable ends get their colliders, or wire dragging will break.");
                return;
            }

            var drag = FindOne<WireDragController>();
            if (drag == null) { problems.Add("No WireDragController in the scene - wires can't be dragged at all."); return; }

            bool included = (drag.wireLayerMask.value & (1 << layer)) != 0;
            if (included)
                problems.Add($"WireDragController > Wire Layer Mask still includes '{cablePlugLayerName}'. " +
                             "Untick it, or the connector's own collider will swallow taps meant for the wires.");

            if (drag.slotLayerMask.value == 0)
                problems.Add("WireDragController > Slot Layer Mask is set to Nothing - wires can never be dropped into a pin.");
        }

        void CheckTasks()
        {
            if (TaskManager.Instance == null)
            {
                notes.Add("No TaskManager in the scene. That's fine, but clear the Task Id fields on " +
                          "CrimpingStationController and NetworkTestStation so they don't report into nothing.");
                return;
            }

            if (FindOne<COC2TaskSetup>() == null)
            {
                var station = FindOne<CrimpingStationController>();
                var tester = FindOne<NetworkTestStation>();
                bool usesTasks = (station != null && !string.IsNullOrEmpty(station.taskId))
                              || (tester != null && !string.IsNullOrEmpty(tester.taskId));
                if (usesTasks)
                    problems.Add("Task Ids are set but there's no COC2TaskSetup in the scene, so those tasks were never " +
                                 "registered with TaskManager. ReportProgress() will silently do nothing.");
            }
        }

        // ------------------------------------------------------------------

        void Report()
        {
            var builder = new StringBuilder();
            builder.AppendLine("===== COC2 setup check =====");

            if (problems.Count == 0)
            {
                builder.AppendLine("No problems found. Every reference this validator knows about is assigned.");
            }
            else
            {
                builder.AppendLine($"{problems.Count} problem(s) to fix:");
                for (int i = 0; i < problems.Count; i++)
                    builder.AppendLine($"  {i + 1}. {problems[i]}");
            }

            if (notes.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"{notes.Count} thing(s) worth knowing (not necessarily wrong):");
                for (int i = 0; i < notes.Count; i++)
                    builder.AppendLine($"  - {notes[i]}");
            }

            builder.Append("============================");

            if (problems.Count > 0) Debug.LogError(builder.ToString(), this);
            else Debug.Log(builder.ToString(), this);
        }

        static T FindOne<T>() where T : Object
        {
            var all = FindAll<T>();
            return all.Length > 0 ? all[0] : null;
        }

        static T[] FindAll<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>(true);
#endif
        }
    }
}
