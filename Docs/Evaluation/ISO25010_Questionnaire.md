# Bayanihan: ISO/IEC 25010 evaluation questionnaire

The Google Form for evaluating the game against ISO/IEC 25010 is built by [`ISO25010_Form.gs`](ISO25010_Form.gs) (a Google Apps Script). This page is the same questionnaire written out, so it can be reviewed, translated or cited before the form is made. It is generated from the script's data, so the two always match.

## How to create the form

1. Open <https://script.google.com>, choose **New project**, delete the sample code and paste the whole of `ISO25010_Form.gs`. Save.
2. Choose the function `createBayanihanForm` in the toolbar and press **Run**.
3. The first run asks for permission to manage your Google Forms (your own script, your own account): Advanced > Go to project > Allow.
4. Open **Execution log**. It prints the edit link and the link to send to respondents.
5. In the form editor, set the two routings the log lists, because Apps Script cannot set them (Google Forms rejects every navigation choice made from a script). On the **Consent** question, "I do not agree" > **Submit form**. On the **Your role in this evaluation** question, "Player..." and "Teacher..." > "Part C Effectiveness". For each question, use its **⋮** menu > **Go to section based on answer**.
6. In the form's **Responses** tab choose **Link to Sheets** to collect answers in a spreadsheet.

Running the script twice makes two forms; delete the extra one from Drive.

## Which edition of ISO/IEC 25010

Product quality follows **ISO/IEC 25010:2023**, as shown on the ISO 25000 reference page (<https://iso25000.com/index.php/en/iso-25000-standards/iso-25010>): 9 characteristics and 39 sub-characteristics.

The reference page does not cover quality in use, so Part C keeps the structure of the 2011 edition: 5 characteristics and 9 sub-characteristics, plus Effectiveness and Efficiency, which have no sub-characteristics in either edition. Check that part against the standard before you cite it.

Compared with the earlier 2011 version of this form: Interaction capability replaces Usability (with four new sub-characteristics, and User interface aesthetics and Accessibility removed), Faultlessness replaces Maturity, Flexibility replaces Portability, and Resistance and the Safety characteristic are new.

## What the counts mean

| Model | Characteristics | Sub-characteristics | Rated items |
|---|---|---|---|
| Product quality (ISO/IEC 25010:2023) | 9 | 39 | 39 |
| Quality in use (2011 structure) | 5 | 9, plus Effectiveness and Efficiency | 11 |
| **Total** | 14 | | **50** |

## Who answers what

| Part | Who | Content | Rated items |
|---|---|---|---|
| Start | Everybody | Consent and a short profile (age group, gaming habit, flood experience, map played, phone OS) | none |
| **A** Playing experience | Everybody | Product-quality items a player can judge by playing: Functional suitability, Performance efficiency, Compatibility, Interaction capability, Reliability | 20 |
| Role question | Everybody | Routes people: IT experts to Part B, everyone else to Part C | none |
| **B** Technical qualities | IT experts only | Items that need the build, the code, or the documentation: Security, Maintainability, Flexibility, Safety | 19 |
| **C** Quality in use | Everybody | What was achieved and how it felt | 11 |
| Overall | Everybody | Overall quality (1 to 10), would you recommend (0 to 10), free comment | none |

Players and teachers rate 20 + 11 = 31 items (about 15 minutes). IT experts rate all 50 items (about 30 minutes). Each statement is a star-rating question. The stars are optional: an empty answer means the respondent cannot judge, and it is treated as missing data when you average. Google Forms star ratings have no N/A option, so leaving the stars empty takes its place.

Each statement is rated with five stars, using this legend (the same legend appears at the top of each characteristic page in the form). Leave the stars empty if you cannot judge.

Stars, from left to right:

- Strongly Disagree
- Disagree
- Neutral
- Agree
- Strongly Agree

## The statements, by sub-characteristic

Each table is one sub-characteristic. The "What it means" column is its definition, and each statement is a separate star-rating question (the heading of the sub-characteristic appears above its statements in the form).

### Part A: Playing experience (everybody)

Please base your answers on what you saw and felt while playing on your phone. There are no right or wrong answers.

#### 1. Functional suitability

The game provides the functions a rescue needs, and they work as they should.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| 1.1 | Functional completeness | Degree to which the set of game functions covers all the rescue tasks and the objectives of the players, from loadout to the after-action report. | (a) The game gave me everything I needed to play a complete mission, from choosing a pack and map to the final report.<br>(b) The actions available (boat, swimming, rescue, first aid, throw lines, items) covered every rescue situation I met. |
| 1.2 | Functional correctness | Degree to which the game gives correct results, with the precision needed, for its controls, timer, score and resident counts. | (a) Each control or action did what I expected (for example tapping a button to board or rescue, to throw a line, or to cut the power with the radio).<br>(b) The timer, score and counts of rescued and registered residents were correct and matched what actually happened. |
| 1.3 | Functional appropriateness | Degree to which the functions help the player accomplish the rescue tasks and goals without unnecessary steps. | (a) The tools I was given were the right ones for finding, reaching and rescuing residents.<br>(b) Delivering residents to the Barangay Hall and registering them took only the steps that were needed. |

#### 2. Performance efficiency

The game is fast enough, and uses the resources it needs in a sensible way.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| 2.1 | Time behaviour | Degree to which the response times, loading times and frame rate of the game, when it performs its functions, meet the needs of play. | (a) The game started, loaded a map and restarted a mission quickly enough that I did not get impatient.<br>(b) The boat, the swimmer and the menus responded to my touches right away. |
| 2.2 | Resource utilization | Degree to which the amounts and types of resources used by the game (processor, graphics, memory, battery and storage) meet the needs of play. | (a) The game ran without making my phone slow, hot or drain its battery too quickly.<br>(b) The storage the game takes up on my phone was reasonable for what the game offers. |
| 2.3 | Capacity | Degree to which the maximum limits of the game, such as residents, items, debris and water in view, and length of play, meet the needs of play. | (a) The game stayed smooth at the busiest moments (many residents, items, debris and water in view at once).<br>(b) Playing several missions in a row, or on either map, did not make the game slower. |

#### 3. Compatibility

The game works alongside other software and with the equipment used to play it.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| 3.1 | Co-existence | Degree to which the game performs its functions alongside other apps on the same phone, without detrimental impact on them or on the game. | (a) I could take a call or switch to other apps during a mission, and go back to the game, without the game or those apps being affected.<br>(b) After I closed the game, my phone settings (such as brightness, volume and display) and other apps were as they were before. |
| 3.2 | Interoperability | Degree to which the game exchanges information with the touch screen and display, and between its own screens, and uses the information exchanged. | (a) The game worked correctly with the touch screen and phone I used.<br>(b) Choices made on the Loadout screen (item pack and map) were carried correctly into the mission. |

#### 4. Interaction capability

Players can use the game interface to understand the game, complete their tasks and stay engaged, in a variety of contexts.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| 4.1 | Appropriateness recognizability | Degree to which players can recognize, from the first screens, whether the game suits their needs and what it is for. | (a) From the first screens I could tell what the game is about and what it is for.<br>(b) Before the mission started I knew what I had to do (rescue residents and register them at the Barangay Hall before time runs out). |
| 4.2 | Learnability | Degree to which players can learn the controls and the rescue tasks quickly and effectively, with the help the game gives. | (a) I learned the controls quickly, without help from another person.<br>(b) The on-screen hints and instructions were enough to learn how to rescue, treat and use items. |
| 4.3 | Operability | Degree to which the game is easy to operate and control, including steering, swimming, interacting and changing the camera with touch. | (a) Steering the boat, swimming, interacting with residents and objects, and adjusting the camera (including first-person view) felt comfortable and easy to control with touch.<br>(b) The on-screen buttons were easy to tap accurately, without my fingers covering the part of the screen I needed to see. |
| 4.4 | User error protection | Degree to which the game protects players against mistakes that cannot be undone or that would end their rescue. | (a) The game warned me about dangers (such as live wires, debris and wreckage) before they could ruin my attempt.<br>(b) A mistake (such as a wrong turn or a wrong item) could easily be corrected and never left me stuck. |
| 4.5 | User engagement | Degree to which the game presents its functions and information in an inviting and motivating way that encourages players to keep interacting. | (a) The screens and the feedback after each action kept me interested and encouraged me to keep playing.<br>(b) The rescue tasks and the information shown during a mission were presented in an inviting way, not as a chore. |
| 4.6 | Inclusivity | Degree to which the game can be used by people of various backgrounds, such as different ages, abilities, cultures and languages. | (a) I could read all on-screen text and symbols comfortably on my phone screen, and I think players of different ages and abilities could too.<br>(b) Triage states and hazards were clear without relying on colour alone or on a particular language. |
| 4.7 | User assistance | Degree to which the game gives players the help they need, at the right moment, to use its functions effectively. | (a) When I needed help, the game gave me hints, control reminders or guidance at the moment I needed them.<br>(b) The help available during a mission was enough for me to recover from a problem without leaving the game. |
| 4.8 | Self-descriptiveness | Degree to which the game shows its capabilities and how to use them on screen, without needing a manual or other outside help. | (a) The game showed what each button, item, resident and hazard was for, so I did not need a manual to understand it.<br>(b) The screens made clear what I could do next without my having to search for information outside the game. |

#### 5. Reliability

The game keeps working under the expected conditions, and recovers when something goes wrong.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| 5.1 | Faultlessness | Degree to which the game performs its functions without fault under normal play, with no crashes, freezes or glitches. | (a) The game ran without crashes, freezes or error messages.<br>(b) I did not notice bugs or glitches (for example objects stuck, residents in odd places). |
| 5.2 | Availability | Degree to which the game is operational and ready to play whenever the player needs it. | (a) The game was ready to play every time I opened it.<br>(b) I could start a mission whenever I wanted, without waiting for downloads, updates or logins. |
| 5.3 | Fault tolerance | Degree to which the game keeps working as intended despite unexpected use or mishaps in play. | (a) The game kept working normally when I did unexpected things (tapping many buttons at once, leaving the boat, driving into obstacles).<br>(b) Hull strikes and other mishaps in play did not break the game. |
| 5.4 | Recoverability | Degree to which, after a mission ends or is interrupted, the game returns to a clean state from which the player can carry on. | (a) After a mission ended, I could quickly retry it or go back to the Loadout.<br>(b) Restarting a mission gave a clean start (residents, items and timer reset correctly). |

Open question: *What problems did you notice while playing (bugs, confusing moments, slowdowns)? Please say where and when it happened.*

### Part B: Technical qualities (IT experts)

This part is for people with software development, testing or security experience. Use the build, and the source and documentation if you were given them. The game is a single-player phone game with no accounts or network, so leave the stars empty whenever a statement truly does not apply or you were not given what you need to judge it.

#### 6. Security

The game defends against tampering and protects the information and data it keeps.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| 6.1 | Confidentiality | Degree to which the data the game keeps, such as personal details, settings, loadout and scores, is accessible only to those allowed to see it. | (a) The game does not collect or show personal or sensitive information (for example names, location, contacts, device identifiers) beyond what it needs.<br>(b) Any data the game saves (settings, loadout, scores) stays on the player's own device and is not sent anywhere else without the player knowing. |
| 6.2 | Integrity | Degree to which the game prevents unauthorised access to, or changes of, its rules, score, timer and data. | (a) The player cannot use the game's controls to change the mission rules, score or timer to get an unearned result.<br>(b) The game rejects invalid input or state (for example registering the same resident twice) instead of corrupting its data. |
| 6.3 | Non-repudiation | Degree to which important mission events, such as rescues, treatments and registrations, can be proven to have taken place. | (a) Important events (a resident rescued, treated or registered; a mission completed) are recorded so they cannot later be denied.<br>(b) The after-action report is a dependable record of what happened in the mission. |
| 6.4 | Accountability | Degree to which the actions of the player can be traced uniquely to the results they produced in the game. | (a) The game can show which of the player's actions led to each result (rescues, first aid, registrations, hull strikes).<br>(b) A play session can be traced afterwards, for example from the report or from logs. |
| 6.5 | Authenticity | Degree to which the game build and the mission results can be proven to be what they claim to be. | (a) I can be confident that the game build I received is genuine and has not been altered.<br>(b) The score and report shown are genuinely those of the mission just played. |
| 6.6 | Resistance | Degree to which the game keeps operating properly when someone tries to attack it or tamper with its files, data or rules. | (a) The game kept working properly when someone tried to tamper with its files, saved data or mission rules, even on a rooted or jailbroken phone.<br>(b) Tampering with the game's settings or saved data did not stop the game from running or corrupt its record of missions. |

#### 7. Maintainability

Developers can understand, change and test the game easily.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| 7.1 | Modularity | Degree to which the game is made of separate parts, such as boat, swimmer, residents, hazards, display, loadout and maps, so that a change to one part has minimal impact on the others. | (a) The game is built from separate parts (for example boat, player, residents, hazards, on-screen display, loadout, maps), so a change to one has little effect on the others.<br>(b) Each map (Manila, Barangay Mabuhay) is its own scene and data that can be changed without touching the core gameplay code. |
| 7.2 | Reusability | Degree to which parts of the game, such as the rescue logic, boat, items and display, can be used in other missions or games. | (a) Parts such as the rescue logic, boat, items and on-screen display could be reused in other missions or games.<br>(b) The same mission logic runs on both maps without being rewritten. |
| 7.3 | Analysability | Degree of effectiveness and efficiency with which a developer can assess the impact of a change, diagnose a defect and find the parts to modify. | (a) The code and documentation make it easy to understand what a feature does and where to find the cause of a defect.<br>(b) The project provides tools that help find problems (for example automated tests, logs, debug output). |
| 7.4 | Modifiability | Degree to which new residents, items, hazards, maps and settings can be added or changed without introducing defects or lowering game quality. | (a) New residents, items, hazards or maps could be added or changed without breaking existing features.<br>(b) Values such as the mission time, scoring and item effects can be adjusted in settings or data, without editing core code. |
| 7.5 | Testability | Degree of effectiveness and efficiency with which test criteria can be set for the game, and tests run by hand or automatically to check them. | (a) Individual features can be tested on their own (for example with automated tests or test scenes).<br>(b) Test criteria can be set and checked automatically (for example a script or bot that plays a whole mission). |

#### 8. Flexibility

The game can be adapted to new screens, systems and contexts, and moved or replaced with little effort.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| 8.1 | Adaptability | Degree to which the game can be adapted to different phone screens, phone sizes, operating systems and graphics settings. | (a) The game adapts to different phone screens and sizes (resolution, aspect ratio, screen cut-outs, graphics quality) without code changes.<br>(b) The game could be adapted to other phone systems (Android and iOS) or to tablets with reasonable effort. |
| 8.2 | Installability | Degree of effectiveness and efficiency with which the game can be installed and uninstalled in its target environment. | (a) The game could be installed and started easily, from the app store or an installer file, by following the instructions given.<br>(b) The game could be uninstalled completely, with no leftover files or effects on other apps. |
| 8.3 | Replaceability | Degree to which a new version of the game, or other training material for the same purpose, can replace an older one without losing saved settings or data. | (a) A new version of the game can replace an older one as an app update, without the player losing settings or data.<br>(b) The game could be used in place of other flood-preparedness training materials (drills, videos, other games) for the same learning aim. |

#### 9. Safety

The game keeps players and residents out of unacceptable risk, and warns them of hazards in time.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| 9.1 | Operational constraint | Degree to which the game keeps the boat and swimmer within safe operating limits when a hazard is encountered. | (a) When a hazard appeared, the game kept the boat and swimmer within safe limits, such as speed and reach.<br>(b) The safety limits the game applies are defined in one place, so they can be checked and changed safely. |
| 9.2 | Risk identification | Degree to which the game identifies situations that could expose life, property or the environment to unacceptable risk. | (a) The game identifies situations that could expose residents or rescuers to serious risk, such as live wires or deep water.<br>(b) Risk situations are defined in the game data or code in a way that developers can review. |
| 9.3 | Fail safe | Degree to which the game places the boat or swimmer in a safe state, or returns it to one, when a failure occurs. | (a) When a failure occurs (for example a stuck control or a hull strike), the game puts the boat or swimmer into a safe state.<br>(b) After a failure the game can revert to a safe condition from which the mission can continue or be restarted. |
| 9.4 | Hazard warning | Degree to which the game warns players of unacceptable risks in time for them to react and keep the rescue safe. | (a) The game warned players about unacceptable risks, such as live wires and debris, early enough for them to react.<br>(b) Hazard warnings are triggered by the game logic, so they follow the actual state of the mission. |
| 9.5 | Safe integration | Degree to which safe operation is kept when the boat, swimmer, residents, hazards and items interact in a mission. | (a) Safe operation was kept when the boat, swimmer, residents, hazards and items interacted during a mission.<br>(b) New items or hazards can be integrated with the existing rescue logic without making the game unsafe. |

Open question: *Any technical concerns or recommendations (security, code structure, installation, phone platforms, safety)?*

### Part C: Quality in use (everybody)

The statements below are about what you achieved and how it felt to use the game for its purpose: rescuing residents of a flooded barangay.

#### Effectiveness

Players achieve the goals of the game accurately and completely. (Effectiveness has no sub-characteristics in ISO/IEC 25010.)

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| Q1 | Effectiveness | Accuracy and completeness with which players achieve the goals of the mission, such as reaching, rescuing, treating and registering residents. | (a) I was able to carry out the tasks the game asked of me (reach, rescue, treat, deliver and register residents).<br>(b) I reached the mission goals as completely and accurately as I intended. |

Also asked (context, not scored): did you finish a mission, and how many of the ten residents did you register in your best attempt (0 to 10).

#### Efficiency

Achieving the goals takes a fitting amount of time and effort. (Efficiency has no sub-characteristics in ISO/IEC 25010.)

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| Q2 | Efficiency | Resources spent by players in relation to the accuracy and completeness with which they achieve the mission goals. | (a) I could complete the mission in a reasonable amount of time.<br>(b) The effort the mission demanded (taps, steps, attention) was in proportion to what I achieved. |

#### Satisfaction

Using the game is satisfying for the player.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| Q3.1 | Usefulness | Degree to which the player is satisfied with the results of playing, such as understanding flood rescue and the value of preparing together. | (a) The game helped me understand what to do in a flood rescue (for example priorities, equipment, teamwork).<br>(b) Playing the game raised my awareness of flood risk and the value of preparing together (bayanihan). |
| Q3.2 | Trust | Degree to which the player has confidence that the game will behave as intended, applying its rules and showing accurate information. | (a) I trusted the game to apply its rules and scoring consistently and fairly.<br>(b) I trusted the information shown (hazard warnings, resident status, timer) to be accurate. |
| Q3.3 | Pleasure | Degree to which the player obtains pleasure from playing the game and from meeting personal needs such as enjoyment. | (a) I enjoyed playing the game.<br>(b) The mission held my interest and I would like to play it again or try the other map. |
| Q3.4 | Comfort | Degree to which the player is physically comfortable while playing, including eyes, hands and balance. | (a) I felt physically comfortable while holding the phone and playing (no eye strain, sore thumbs or headache).<br>(b) The movement of the camera and the water did not cause me dizziness or discomfort. |

#### Freedom from risk

Using the game avoids harm to people, money and the environment.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| Q4.1 | Economic risk mitigation | Degree to which playing the game limits the risk to the financial status of players, schools, barangays and their property. | (a) Using the game costs little or nothing, including data use, and needs no special equipment beyond an ordinary phone.<br>(b) A school, barangay or community group could adopt the game without a significant financial risk. |
| Q4.2 | Health and safety risk mitigation | Degree to which playing the game limits the risk to the health and safety of people, and shows flood hazards in a way that supports safe behaviour. | (a) Playing the game did not put my health at risk (for example no flashing visuals, no pressure to play for long periods).<br>(b) The game shows flood hazards and people in danger respectfully and in a way that encourages safe behaviour in real life. |
| Q4.3 | Environmental risk mitigation | Degree to which playing the game limits the risk to the environment, through energy and battery use and hardware needs, and promotes responsible behaviour. | (a) Running the game used a reasonable amount of battery and electricity and did not require buying a new phone.<br>(b) The game encourages environmentally responsible behaviour, for example about waste, drainage and flood preparedness. |

#### Context coverage

The game works in the situations it was made for, and beyond them.

| # | Sub-characteristic | What it means | Statements, each rated with stars |
|---|---|---|---|
| Q5.1 | Context completeness | Degree to which the game can be used with effectiveness, efficiency, freedom from risk and satisfaction in all the specified contexts of use, such as home, school and on the go. | (a) The game was fully usable in the places and situations where I used it (for example at home, at school, or on the go).<br>(b) Whichever pack of items I chose, I could still complete rescues on the map I played. |
| Q5.2 | Flexibility | Degree to which the game can be used with quality in contexts beyond those first specified, such as other rescue situations or classroom and community training. | (a) I could change my approach (map, item pack, route, boat or swimming) to suit different rescue situations and still succeed.<br>(b) The game could also serve other purposes and users, such as classroom lessons or community flood-preparedness training. |

Open question: *What did you like best about the game, and what should we change first?*

## Reading the results

Link the form to Sheets. Each rating column header starts with the sub-characteristic number and the letter of the statement, for example `1.1a  The game gave me everything I needed...`, so every column maps to one sub-characteristic (the tables above give the names). Take the number of stars in each answer, treat an empty answer as missing data, and average the two statements of a sub-characteristic, then average sub-characteristics to get the characteristic score. Report the two groups apart (players and teachers on one side, IT experts on the other), because they answer different parts. A common verbal reading of a 5-point mean is: 1.00 to 1.80 strongly disagree, 1.81 to 2.60 disagree, 2.61 to 3.40 neutral, 3.41 to 4.20 agree, 4.21 to 5.00 strongly agree; use whichever interpretation your school or adviser prescribes.

## Choices worth reviewing

- **Two statements per sub-characteristic** keeps the form to about 15 to 30 minutes while giving each of the 50 items an average. Add a third statement to any grid by adding a string to its `statements` array.
- **Star ratings.** Each statement is its own star-rating question, so the form has about twice as many rating questions as before, and each sub-characteristic has a heading above its statements. Google Forms star ratings have no N/A option, so the stars are optional. Empty answers are treated as "cannot judge".
- **Safety is in Part B.** Two of its five sub-characteristics (operational constraint and safe integration) need the build or the design documents, so they are asked of IT experts. This keeps the ISO numbering in order (1 to 9). To have players rate Safety, move the characteristic into Part A; its numbers would then no longer follow the standard.
- **Part B is for IT experts**, since confidentiality, non-repudiation, accountability, authenticity, resistance, modularity, reusability, analysability, modifiability, testability, adaptability, installability, replaceability cannot be judged by playing alone. Safety is kept in this part for its numbering (see above). Bayanihan is an offline single-player phone game with no accounts, so several Security statements are about design intent and the report; experts may leave the stars empty.
- **New statements.** User engagement, Inclusivity, User assistance, Self-descriptiveness, Resistance and the five Safety sub-characteristics are new, and their statements were written for this version. Inclusivity was adapted from the earlier Accessibility statements. Check them with the team before the form goes out.
- **User assistance.** The reference page words this sub-characteristic like an accessibility definition, so the form defines it by its purpose instead: help available to players during play. Check the wording against the standard.
- **Wording describes a mobile phone game, as planned.** The phone build is still being implemented, so check the controls, screens and performance statements against it before the form goes out. The wording covers the parts of the game (boat and swimmer, ten residents, Loadout, Manila and Barangay Mabuhay maps, Barangay Hall registration, after-action report). The weather code can play rain and thunder sound, but the project has no audio clips yet, so there is no sound to rate. If the team wants sound judged, add a statement (for example under User engagement).
- **Control examples.** The control examples in Part A (tapping to board or rescue, a button to throw a line, a button to cut the power) are placeholders until the phone controls are final. Replace them with the real control names.
- **Consent text** is a plain default. If your school requires its own consent or data-privacy wording (for example for respondents under 18), replace it in `introText_()` and `addConsentAndProfile_()` in the script.
- **Contact line**: set `CONTACT` at the top of the script to show your group's contact; it is empty by default.
