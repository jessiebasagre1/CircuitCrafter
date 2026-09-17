#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// QUIZ CONTENT for COC 3, in the same style as COC3StepBlueprint: edit the table here, then run
/// Tools ▸ COC3 ▸ Generate Quiz Assets to turn it into QuizQuestionData and QuizSetData assets.
///
/// Four sets, at the learning-outcome boundaries the bridge fires at:
///
///   after step 11   LO2 - configure network services
///   after step 17   LO1 - set up user access
///   after step 30   LO3 - test the configuration
///   after step 35   mixed review, at the end of the activity
///
/// Each set holds EIGHT questions and asks THREE at random, so a retry is not the same quiz
/// twice and a learner cannot pass by memorising an order.
///
/// THE QUESTIONS TEST UNDERSTANDING, NOT RECALL.
/// "What did you type as the IP address?" proves nothing - they typed it ten minutes ago and the
/// hint would have told them. "Why must a server not lease its own address from DHCP?" is what an
/// assessor actually asks, and it is the difference between someone who followed instructions and
/// someone who could do the job without them.
///
/// NO BACKSLASHES IN QUESTION TEXT. TextMeshPro eats one of every pair unless Parse Escape
/// Characters is off, and the quiz view is not ours to change - so a UNC path is described in
/// words rather than written out. That is better for a question anyway: naming the shape of the
/// path is the knowledge, copying it is not.
/// </summary>
public static class COC3QuizBlueprint
{
    public class Question
    {
        public string text;
        public string[] choices;
        public int correct;
        public int points = 10;
        public string explanation;
    }

    public class Set
    {
        public string assetName;
        public string taskId;         // label only - the bridge calls StartQuiz directly
        public int questionsToAsk = 3;
        public List<Question> pool = new List<Question>();
    }

    private static Question Q(string text, string[] choices, int correct, string explanation,
                              int points = 10)
    {
        return new Question
        {
            text = text, choices = choices, correct = correct,
            explanation = explanation, points = points
        };
    }

    public static readonly List<Set> Sets = new List<Set>
    {
        // ==============================================================
        // LO2 - after step 11
        // ==============================================================
        new Set
        {
            assetName = "COC3_Quiz_LO2_NetworkServices",
            taskId = "COC3_LO2",
            questionsToAsk = 3,
            pool = new List<Question>
            {
                Q("Why must a server use a static IP address instead of taking one from DHCP?",
                  new[]
                  {
                      "Static addresses are faster than leased ones",
                      "Clients and services find the server by an address that must never change",
                      "DHCP cannot issue addresses to server operating systems",
                      "A static address uses less bandwidth"
                  }, 1,
                  "A lease can change. Every client, every DNS record and every mapped drive that " +
                  "points at this machine would break when it did."),

                Q("The server holds 192.168.10.10. Why does the DHCP scope start at .100?",
                  new[]
                  {
                      "Addresses below .100 are reserved by Windows",
                      "A scope must always start on a round number",
                      "So the pool can never hand a client an address already in use statically",
                      "To leave room for a second DHCP server"
                  }, 2,
                  "A scope that overlapped .10 would eventually lease the server's own address to " +
                  "a client, and two machines answering to one address is a fault that comes and " +
                  "goes with the lease."),

                Q("Why is the server's preferred DNS server set to 127.0.0.1?",
                  new[]
                  {
                      "It is hosting DNS itself, so it resolves names by asking itself",
                      "127.0.0.1 is the fastest DNS server available",
                      "It stops the server from reaching the internet",
                      "Domain controllers are not allowed to use a real DNS address"
                  }, 0,
                  "The loopback address points the machine at its own DNS service. Pointing it " +
                  "elsewhere would leave a domain controller unable to resolve its own domain."),

                Q("Promoting the server created a forward lookup zone automatically. Why did you " +
                  "still have to create a reverse zone by hand?",
                  new[]
                  {
                      "The forward zone was created incorrectly",
                      "Reverse zones are only needed on client machines",
                      "Promotion creates only what the domain itself needs; reverse resolution is optional",
                      "Reverse zones cannot be created automatically by any version of Windows"
                  }, 2,
                  "A domain works without reverse lookup. It is needed for turning an address back " +
                  "into a name - diagnostics, logging, and some services that check the caller."),

                Q("What does a reverse lookup zone actually do?",
                  new[]
                  {
                      "Resolves an IP address back to a host name",
                      "Reverses the order of DNS server priority",
                      "Restores DNS records after a failure",
                      "Resolves a host name to an IP address"
                  }, 0,
                  "Forward resolves name to address. Reverse does the opposite, which is what a " +
                  "log entry or a diagnostic tool needs when all it has is the address."),

                Q("Why must a client be joined to the domain before it can use domain accounts?",
                  new[]
                  {
                      "Domain accounts are stored on the client once it joins",
                      "It is only a licensing requirement",
                      "Joining installs Active Directory on the client",
                      "Only a member computer can ask the domain controller to authenticate a sign-in"
                  }, 3,
                  "A workgroup machine has no trust relationship with the domain, so it has nobody " +
                  "to ask. It also receives no Group Policy, which is how everything later in " +
                  "this job order reaches it."),

                Q("Installing the AD DS role did not create the domain. What did?",
                  new[]
                  {
                      "Restarting the server after installation",
                      "Promoting the server to a domain controller",
                      "Creating the first user account",
                      "Setting the computer name"
                  }, 1,
                  "Installing a role puts the software on the machine. Promotion is the separate " +
                  "step that creates the forest, the domain and the directory itself."),

                Q("A DHCP scope hands out a router address with every lease. What is it for?",
                  new[]
                  {
                      "It tells the client which DNS server to use",
                      "It identifies the DHCP server that issued the lease",
                      "It is the default gateway - where the client sends traffic for other networks",
                      "It sets the client's subnet mask"
                  }, 2,
                  "Without a gateway a client can reach its own subnet and nothing else. It is the " +
                  "commonest reason a machine can ping its neighbours but not the internet.")
            }
        },

        // ==============================================================
        // LO1 - after step 17
        // ==============================================================
        new Set
        {
            assetName = "COC3_Quiz_LO1_UserAccess",
            taskId = "COC3_LO1",
            questionsToAsk = 3,
            pool = new List<Question>
            {
                Q("Why are permissions granted to a GROUP rather than to each user account?",
                  new[]
                  {
                      "Windows does not allow permissions on individual accounts",
                      "Group permissions are processed faster",
                      "Groups use less disk space in the directory",
                      "Access follows the group, so adding or removing a person is one change"
                  }, 3,
                  "When a student leaves you remove them from the group once, and every permission " +
                  "the group holds goes with them. Per-user permissions have to be hunted down " +
                  "one folder at a time."),

                Q("Why must STUDENTS be a SECURITY group rather than a distribution group?",
                  new[]
                  {
                      "Only a security group can be given permissions",
                      "Distribution groups cannot contain user accounts",
                      "Security groups are visible in the directory and distribution groups are not",
                      "A distribution group cannot be renamed"
                  }, 0,
                  "A distribution group is a mailing list. It can hold members, but it can never " +
                  "appear on an access control list."),

                Q("The group was given Modify rather than Full control. What does Full control add?",
                  new[]
                  {
                      "The ability to open files that are in use",
                      "Nothing - the two are the same on a folder",
                      "Unlimited storage in the folder",
                      "The right to change the permissions and take ownership"
                  }, 3,
                  "Modify covers reading, writing and deleting the group's own work. Full control " +
                  "lets the group rewrite the very permissions you just set, which defeats the " +
                  "point of setting them."),

                Q("A folder is shared but its Security tab denies the group access. What happens?",
                  new[]
                  {
                      "They can browse the folder but not open files",
                      "Access is refused - the stricter of share and NTFS permissions wins",
                      "The share permission wins because it is checked first",
                      "Access is allowed because sharing overrides NTFS"
                  }, 1,
                  "Share permissions and NTFS permissions are both checked, and the more " +
                  "restrictive result applies. This is why a share that 'should work' often does " +
                  "not."),

                Q("What is the difference between the folder name on disk and the share name?",
                  new[]
                  {
                      "The share name is what clients type; the folder name is what the disk holds",
                      "They must always be identical",
                      "The share name is only used by administrators",
                      "The folder name is case sensitive and the share name is not"
                  }, 0,
                  "They are independent. Clients reach the share by its share name and never need " +
                  "to know what the folder is called on the server's disk."),

                Q("The password policy was set but the 'Define this policy setting' box was left " +
                  "clear. What is the effect?",
                  new[]
                  {
                      "The policy applies only to administrators",
                      "The policy applies after the next restart",
                      "The policy is not applied at all",
                      "The policy applies with a default value of 8"
                  }, 2,
                  "An undefined setting is simply not applied. The value in the box is ignored, " +
                  "and nothing warns you - which is what makes it a quiet failure."),

                Q("Why does a new user account belong in the Users container rather than anywhere " +
                  "else in the directory?",
                  new[]
                  {
                      "Accounts elsewhere cannot sign in",
                      "Only the Users container supports passwords",
                      "It is where anyone maintaining the domain will look for it",
                      "Windows deletes accounts that are not in Users"
                  }, 2,
                  "An account in Computers or the domain root works perfectly well - and the next " +
                  "technician will not find it. Predictable placement is part of the job."),

                Q("What does a GLOBAL group scope mean?",
                  new[]
                  {
                      "It can hold accounts from this domain and be used anywhere in the forest",
                      "It is visible to every computer on the internet",
                      "It holds every account in the domain automatically",
                      "It can only be used on the domain controller"
                  }, 0,
                  "Global is the normal scope for a group of people from one domain. Domain local " +
                  "is for granting access within one domain; universal spans a whole forest.")
            }
        },

        // ==============================================================
        // LO3 - after step 30
        // ==============================================================
        new Set
        {
            assetName = "COC3_Quiz_LO3_Testing",
            taskId = "COC3_LO3",
            questionsToAsk = 3,
            pool = new List<Question>
            {
                Q("Why is every test in the plan performed on the CLIENT rather than on the server?",
                  new[]
                  {
                      "The server cannot run the test tools",
                      "Client hardware is faster for testing",
                      "A service checked only on the server has not been tested as a user would use it",
                      "Testing on a server can corrupt the configuration"
                  }, 2,
                  "The server can reach its own folder whatever the share and permissions say. " +
                  "Only the client exercises the network, the name resolution, the share and the " +
                  "permissions together."),

                Q("Why write the test plan BEFORE doing any testing?",
                  new[]
                  {
                      "It is required by the software",
                      "It is faster than testing as you go",
                      "So nothing is skipped and the pass criteria are not decided after the fact",
                      "So the client can be billed for it separately"
                  }, 2,
                  "A plan written afterwards is a description of what happened to work. Written " +
                  "first, it is a standard the work has to meet."),

                Q("What shape does a path to a network share take?",
                  new[]
                  {
                      "A drive letter followed by the share name",
                      "Two backslashes, the server name, one backslash, then the SHARE name",
                      "The server's IP address followed by a colon and a port",
                      "Two forward slashes, the domain, then the folder path on disk"
                  }, 1,
                  "It names the server and the share, not the folder on the server's disk. That is " +
                  "why the share name matters and the folder name does not."),

                Q("Folder redirection points Documents at a path on the server. Why must that path " +
                  "be a network path and not a local one?",
                  new[]
                  {
                      "Local paths are slower to open",
                      "Windows blocks local paths in Group Policy",
                      "The client's C: drive is not the server's C: drive",
                      "A local path cannot contain a user name"
                  }, 2,
                  "A local path would redirect every user to a folder on their own machine, which " +
                  "is the opposite of what redirection is for."),

                Q("The test is signed in as student01 rather than as an administrator. Why does " +
                  "that matter?",
                  new[]
                  {
                      "Administrators cannot use Remote Desktop",
                      "An administrator can always connect, so it would prove nothing about the permission",
                      "Administrator sessions are not logged",
                      "The administrator account has no redirected Documents folder"
                  }, 1,
                  "Administrators bypass the very setting being tested. A test that only passes " +
                  "for an administrator has tested nothing."),

                Q("Remote Desktop is enabled and the user is listed, but the connection still " +
                  "fails. What is the most likely cause?",
                  new[]
                  {
                      "The firewall is blocking the inbound rule",
                      "The user's password is too short",
                      "The server needs a second network adapter",
                      "Remote Desktop cannot work on a domain controller"
                  }, 0,
                  "A server that blocks its own service is the commonest cause of a connection " +
                  "that should work and does not."),

                Q("The Remote Desktop inbound rule is disabled. Which is the correct fix?",
                  new[]
                  {
                      "Turn Windows Defender Firewall off",
                      "Enable the inbound rule for Remote Desktop",
                      "Move the server to a different subnet",
                      "Change Remote Desktop to a different port"
                  }, 1,
                  "Turning the firewall off would also make it work, and would leave every other " +
                  "port open too. The rule is the fix, not the firewall."),

                Q("A ping to the server succeeds but the share will not open. What does that tell " +
                  "you?",
                  new[]
                  {
                      "The server is switched off",
                      "The cable is faulty",
                      "The client has no IP address",
                      "The network is fine, so the fault is in sharing, permissions or name resolution"
                  }, 3,
                  "Ping proves the two machines can reach each other. Everything above that layer " +
                  "is still in question, which is exactly why the tests are ordered this way.")
            }
        },

        // ==============================================================
        // Final review - after step 35
        // ==============================================================
        new Set
        {
            assetName = "COC3_Quiz_Final_Review",
            taskId = "COC3_Final",
            questionsToAsk = 3,
            pool = new List<Question>
            {
                Q("What does 'Effective Access' show that reading the permission list does not?",
                  new[]
                  {
                      "The date each permission was granted",
                      "What an account can actually do once every group and inherited entry is resolved",
                      "Which administrator set each permission",
                      "How much of the folder the account has used"
                  }, 1,
                  "A person can be in several groups, and entries can arrive from a parent folder. " +
                  "Effective Access is the only view that answers what they ended up with."),

                Q("The folder's permission list shows Everyone with Full control, inherited from " +
                  "the parent folder. Removing it from this folder alone does nothing. Why?",
                  new[]
                  {
                      "Everyone cannot be removed from any folder",
                      "The entry is protected by Windows",
                      "It has to be removed from the share permissions instead",
                      "While inheritance is on, the entry comes straight back from the parent"
                  }, 3,
                  "Inheritance has to be disabled first, which turns the inherited entries into " +
                  "this folder's own. Only then can one be removed and stay removed."),

                Q("In the Security log, why filter for Audit Failure rather than reading everything?",
                  new[]
                  {
                      "Audit Success entries are not recorded by default",
                      "Failures are the short list worth reading; successes are thousands of normal events",
                      "Audit Failure entries are deleted after 24 hours",
                      "Only failures contain the user name"
                  }, 1,
                  "Every normal sign-in and every permitted file open is an Audit Success. The " +
                  "refusals are the ones that say something."),

                Q("No Audit Failure entries appear at all. What can you conclude?",
                  new[]
                  {
                      "That nobody has attempted unauthorised access - which is not the same as being secure",
                      "That the permissions are definitely correct",
                      "That auditing is broken",
                      "That the log has been cleared"
                  }, 0,
                  "An empty list can mean the restrictions hold, or that nobody has tested them. " +
                  "Absence of evidence is not evidence of correct configuration."),

                Q("Why does the job order end with a service report?",
                  new[]
                  {
                      "It is needed to close the software licence",
                      "It is the only record of what was configured, what was tested and what the result was",
                      "It is required before the server can be restarted",
                      "It replaces the test plan"
                  }, 1,
                  "Undocumented work cannot be handed over, verified, or repeated by whoever comes " +
                  "next. The report is what the client is actually paying for."),

                Q("A printer is shared but not listed in the directory. What is the consequence?",
                  new[]
                  {
                      "Nobody can print to it at all",
                      "Only administrators can use it",
                      "Clients cannot find it by name - every client becomes a manual setup",
                      "The printer will not accept a test page"
                  }, 2,
                  "Sharing puts it on the network. Publishing it to the domain is what lets a " +
                  "client search for it by name instead of being told its address and driver."),

                Q("Everyone was removed from the printer's permissions before STUDENTS was granted " +
                  "Print. Why does the order matter?",
                  new[]
                  {
                      "Windows requires removals before additions",
                      "It does not matter - the result is the same either way",
                      "Granting Print twice would cause an error",
                      "While Everyone can print, restricting the group changes nothing"
                  }, 3,
                  "A permission granted underneath a broader one is not a restriction. Everyone " +
                  "has to go before anything you grant the group means anything."),

                Q("Which of these is the best evidence that the whole print chain works?",
                  new[]
                  {
                      "The printer appears in Devices and Printers on the client",
                      "The printer's status shows Ready on the server",
                      "A test page printed from the client comes out",
                      "The share name is correct"
                  }, 2,
                  "A page on the tray proves sharing, name resolution, driver installation and the " +
                  "Print permission all at once. Any one of them broken and nothing comes out.")
            }
        }
    };
}
#endif
