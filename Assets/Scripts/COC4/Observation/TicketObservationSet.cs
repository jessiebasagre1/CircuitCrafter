using System;
using UnityEngine;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The things a learner can do to find out what is wrong with this machine.
    ///
    /// Create with: Create &gt; CircuitCrafter &gt; Ticket Observations
    ///
    /// WHY THIS EXISTS
    /// TicketData carries one symptomText, and the diagnostic panel shows it the
    /// instant the scene loads. So the learner is handed the observation before
    /// going anywhere near the machine - and observing is the skill being taught.
    ///
    /// This breaks that one paragraph into separate actions the learner has to
    /// choose to take. Press the power button and listen. Look at the screen.
    /// Open the case. Each costs time, and each reveals only its own part of the
    /// picture. Put together they say what symptomText used to say in one go, but
    /// the learner had to decide what was worth looking at.
    ///
    /// AUTHORING ADVICE
    /// Make the cheap observations narrow the field and the expensive one confirm
    /// it. A learner who opens the case first will get the answer, and will have
    /// spent the most time getting it - which is exactly the trade a real
    /// technician makes.
    ///
    /// One of these per ticket. TicketData is not modified.
    /// </summary>
    [CreateAssetMenu(fileName = "Observations", menuName = "CircuitCrafter/Ticket Observations")]
    public class TicketObservationSet : ScriptableObject
    {
        [Tooltip("Which ticket these belong to. Must exactly match a TicketData's Ticket Id.")]
        public string ticketId = "";

        [Tooltip("Everything the learner can check at this site, in the order they should appear.")]
        public Observation[] observations = new Observation[0];

        [Serializable]
        public class Observation
        {
            [Tooltip("Short unique id within this set, e.g. \"power_on\". Used to record what was checked.")]
            public string observationId = "";

            [Tooltip("What the learner clicks, written as an action: \"Press the power button and listen\".")]
            public string action = "";

            [TextArea(2, 5)]
            [Tooltip("What they find. Write it as an observation, not a conclusion - \"no beep of any kind\", not \"the CPU is not seated\".")]
            public string result = "";

            [Tooltip("Seconds this adds to the service call. Make looking inside the case cost more than pressing a button.")]
            [Min(0)]
            public int timeCostSeconds = 15;
        }

        public Observation Find(string observationId)
        {
            foreach (var observation in observations)
            {
                if (observation != null && observation.observationId == observationId) return observation;
            }
            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var observation in observations)
            {
                if (observation == null) continue;

                if (string.IsNullOrWhiteSpace(observation.observationId))
                    Debug.LogWarning($"[TicketObservationSet] '{name}' has an observation with a blank id - it can't be recorded.", this);
                else if (!seen.Add(observation.observationId))
                    Debug.LogWarning($"[TicketObservationSet] '{name}' uses the id '{observation.observationId}' twice.", this);
            }
        }
#endif
    }
}
