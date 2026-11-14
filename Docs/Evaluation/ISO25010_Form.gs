/**
 * Bayanihan - ISO/IEC 25010 evaluation form (Google Apps Script)
 *
 * HOW TO RUN (one minute, nothing to install)
 *   1. Open https://script.google.com and click "New project".
 *   2. Delete the sample code, paste this whole file, click Save.
 *   3. Pick the function  createBayanihanForm  in the toolbar and click Run.
 *   4. The first run asks you to authorise the script (it needs Google Forms only).
 *      Choose your account > Advanced > "Go to project (unsafe)" > Allow. This is normal for your own script.
 *   5. Open "Execution log": it prints the edit link, the link to send to respondents, and the two routing
 *      settings to make by hand in the form editor (Apps Script cannot set them).
 *
 * Running it twice creates a second form; delete the extra one from Google Drive.
 *
 * WHAT THE FORM COVERS
 *   Product quality: ISO/IEC 25010:2023 (the edition on the ISO 25000 reference page): 9 characteristics and 39
 *   sub-characteristics.
 *   Quality in use: 5 characteristics and 9 sub-characteristics, plus Effectiveness and Efficiency. The reference page does
 *   not cover quality in use, so this part keeps the 2011 structure.
 *   Every sub-characteristic has a heading and two star-rating statements about the game, numbered 1.1 ... 9.5 and
 *   Q1 ... Q5.2, so the answers map straight back to the standard: 50 rated items in all.
 *
 * WHO ANSWERS WHAT (a role question at the end of Part A decides who answers Part B. The script cannot set that routing,
 *                   because Google Forms rejects navigation choices made through Apps Script, so the run log lists the two
 *                   routing settings to make by hand in the form editor: the consent exit and the role routing)
 *   Part A  Playing experience     - everybody         (20 sub-characteristics a player can judge by playing)
 *   Part B  Technical qualities    - IT experts only   (19 sub-characteristics that need the build, the code or the docs)
 *   Part C  Quality in use         - everybody         (9 sub-characteristics + Effectiveness and Efficiency)
 *
 * Wording is written for the Bayanihan flood-rescue game as a mobile phone game (boat + swimmer, ten residents, Loadout, two maps,
 * Barangay Hall registration, after-action report). Edit the statements in QUESTIONNAIRE below if the game changes.
 */

// ----- things you may want to change -------------------------------------------------------------------------------
const FORM_TITLE = 'Bayanihan Flood Rescue Game: Software Quality Evaluation (ISO/IEC 25010)';
const CONTACT = '';                    // e.g. 'Team Bayanihan, your.group@example.com'. Left empty = no contact line is shown.
const CONFIRMATION = 'Salamat! Thank you for helping us make Bayanihan better. Your response has been recorded.';

// ----- the questionnaire ---------------------------------------------------------------------------------------------
// part > characteristic > sub-characteristic (grid) > two statements (rows)
const QUESTIONNAIRE = [
  {
    id: 'A',
    label: 'Part A',
    audience: 'everybody',
    title: 'Playing experience',
    intro: 'Please base your answers on what you saw and felt while playing on your phone. There are no right or wrong answers.',
    characteristics: [
      {
        id: '1', name: 'Functional suitability',
        blurb: 'The game provides the functions a rescue needs, and they work as they should.',
        subs: [
          { id: '1.1', name: 'Functional completeness',
            meaning: 'Degree to which the set of game functions covers all the rescue tasks and the objectives of the players, from loadout to the after-action report.',
            statements: [
              'The game gave me everything I needed to play a complete mission, from choosing a pack and map to the final report.',
              'The actions available (boat, swimming, rescue, first aid, throw lines, items) covered every rescue situation I met.'
            ] },
          { id: '1.2', name: 'Functional correctness',
            meaning: 'Degree to which the game gives correct results, with the precision needed, for its controls, timer, score and resident counts.',
            statements: [
              'Each control or action did what I expected (for example tapping a button to board or rescue, to throw a line, or to cut the power with the radio).',
              'The timer, score and counts of rescued and registered residents were correct and matched what actually happened.'
            ] },
          { id: '1.3', name: 'Functional appropriateness',
            meaning: 'Degree to which the functions help the player accomplish the rescue tasks and goals without unnecessary steps.',
            statements: [
              'The tools I was given were the right ones for finding, reaching and rescuing residents.',
              'Delivering residents to the Barangay Hall and registering them took only the steps that were needed.'
            ] }
        ]
      },
      {
        id: '2', name: 'Performance efficiency',
        blurb: 'The game is fast enough, and uses the resources it needs in a sensible way.',
        subs: [
          { id: '2.1', name: 'Time behaviour',
            meaning: 'Degree to which the response times, loading times and frame rate of the game, when it performs its functions, meet the needs of play.',
            statements: [
              'The game started, loaded a map and restarted a mission quickly enough that I did not get impatient.',
              'The boat, the swimmer and the menus responded to my touches right away.'
            ] },
          { id: '2.2', name: 'Resource utilization',
            meaning: 'Degree to which the amounts and types of resources used by the game (processor, graphics, memory, battery and storage) meet the needs of play.',
            statements: [
              'The game ran without making my phone slow, hot or drain its battery too quickly.',
              'The storage the game takes up on my phone was reasonable for what the game offers.'
            ] },
          { id: '2.3', name: 'Capacity',
            meaning: 'Degree to which the maximum limits of the game, such as residents, items, debris and water in view, and length of play, meet the needs of play.',
            statements: [
              'The game stayed smooth at the busiest moments (many residents, items, debris and water in view at once).',
              'Playing several missions in a row, or on either map, did not make the game slower.'
            ] }
        ]
      },
      {
        id: '3', name: 'Compatibility',
        blurb: 'The game works alongside other software and with the equipment used to play it.',
        subs: [
          { id: '3.1', name: 'Co-existence',
            meaning: 'Degree to which the game performs its functions alongside other apps on the same phone, without detrimental impact on them or on the game.',
            statements: [
              'I could take a call or switch to other apps during a mission, and go back to the game, without the game or those apps being affected.',
              'After I closed the game, my phone settings (such as brightness, volume and display) and other apps were as they were before.'
            ] },
          { id: '3.2', name: 'Interoperability',
            meaning: 'Degree to which the game exchanges information with the touch screen and display, and between its own screens, and uses the information exchanged.',
            statements: [
              'The game worked correctly with the touch screen and phone I used.',
              'Choices made on the Loadout screen (item pack and map) were carried correctly into the mission.'
            ] }
        ]
      },
      {
        id: '4', name: 'Interaction capability',
        blurb: 'Players can use the game interface to understand the game, complete their tasks and stay engaged, in a variety of contexts.',
        subs: [
          { id: '4.1', name: 'Appropriateness recognizability',
            meaning: 'Degree to which players can recognize, from the first screens, whether the game suits their needs and what it is for.',
            statements: [
              'From the first screens I could tell what the game is about and what it is for.',
              'Before the mission started I knew what I had to do (rescue residents and register them at the Barangay Hall before time runs out).'
            ] },
          { id: '4.2', name: 'Learnability',
            meaning: 'Degree to which players can learn the controls and the rescue tasks quickly and effectively, with the help the game gives.',
            statements: [
              'I learned the controls quickly, without help from another person.',
              'The on-screen hints and instructions were enough to learn how to rescue, treat and use items.'
            ] },
          { id: '4.3', name: 'Operability',
            meaning: 'Degree to which the game is easy to operate and control, including steering, swimming, interacting and changing the camera with touch.',
            statements: [
              'Steering the boat, swimming, interacting with residents and objects, and adjusting the camera (including first-person view) felt comfortable and easy to control with touch.',
              'The on-screen buttons were easy to tap accurately, without my fingers covering the part of the screen I needed to see.'
            ] },
          { id: '4.4', name: 'User error protection',
            meaning: 'Degree to which the game protects players against mistakes that cannot be undone or that would end their rescue.',
            statements: [
              'The game warned me about dangers (such as live wires, debris and wreckage) before they could ruin my attempt.',
              'A mistake (such as a wrong turn or a wrong item) could easily be corrected and never left me stuck.'
            ] },
          { id: '4.5', name: 'User engagement',
            meaning: 'Degree to which the game presents its functions and information in an inviting and motivating way that encourages players to keep interacting.',
            statements: [
              'The screens and the feedback after each action kept me interested and encouraged me to keep playing.',
              'The rescue tasks and the information shown during a mission were presented in an inviting way, not as a chore.'
            ] },
          { id: '4.6', name: 'Inclusivity',
            meaning: 'Degree to which the game can be used by people of various backgrounds, such as different ages, abilities, cultures and languages.',
            statements: [
              'I could read all on-screen text and symbols comfortably on my phone screen, and I think players of different ages and abilities could too.',
              'Triage states and hazards were clear without relying on colour alone or on a particular language.'
            ] },
          { id: '4.7', name: 'User assistance',
            meaning: 'Degree to which the game gives players the help they need, at the right moment, to use its functions effectively.',
            statements: [
              'When I needed help, the game gave me hints, control reminders or guidance at the moment I needed them.',
              'The help available during a mission was enough for me to recover from a problem without leaving the game.'
            ] },
          { id: '4.8', name: 'Self-descriptiveness',
            meaning: 'Degree to which the game shows its capabilities and how to use them on screen, without needing a manual or other outside help.',
            statements: [
              'The game showed what each button, item, resident and hazard was for, so I did not need a manual to understand it.',
              'The screens made clear what I could do next without my having to search for information outside the game.'
            ] }
        ]
      },
      {
        id: '5', name: 'Reliability',
        blurb: 'The game keeps working under the expected conditions, and recovers when something goes wrong.',
        subs: [
          { id: '5.1', name: 'Faultlessness',
            meaning: 'Degree to which the game performs its functions without fault under normal play, with no crashes, freezes or glitches.',
            statements: [
              'The game ran without crashes, freezes or error messages.',
              'I did not notice bugs or glitches (for example objects stuck, residents in odd places).'
            ] },
          { id: '5.2', name: 'Availability',
            meaning: 'Degree to which the game is operational and ready to play whenever the player needs it.',
            statements: [
              'The game was ready to play every time I opened it.',
              'I could start a mission whenever I wanted, without waiting for downloads, updates or logins.'
            ] },
          { id: '5.3', name: 'Fault tolerance',
            meaning: 'Degree to which the game keeps working as intended despite unexpected use or mishaps in play.',
            statements: [
              'The game kept working normally when I did unexpected things (tapping many buttons at once, leaving the boat, driving into obstacles).',
              'Hull strikes and other mishaps in play did not break the game.'
            ] },
          { id: '5.4', name: 'Recoverability',
            meaning: 'Degree to which, after a mission ends or is interrupted, the game returns to a clean state from which the player can carry on.',
            statements: [
              'After a mission ended, I could quickly retry it or go back to the Loadout.',
              'Restarting a mission gave a clean start (residents, items and timer reset correctly).'
            ] }
        ],
        comments: {
          title: 'Part A comments (optional)',
          prompt: 'What problems did you notice while playing (bugs, confusing moments, slowdowns)? Please say where and when it happened.'
        }
      }
    ]
  },
  {
    id: 'B',
    label: 'Part B',
    audience: 'IT experts',
    title: 'Technical qualities',
    intro: 'This part is for people with software development, testing or security experience. Use the build, and the source and ' +
           'documentation if you were given them. The game is a single-player phone game with no accounts or network, so leave ' +
           'the stars empty whenever a statement truly does not apply or you were not given what you need to judge it.',
    characteristics: [
      {
        id: '6', name: 'Security',
        blurb: 'The game defends against tampering and protects the information and data it keeps.',
        subs: [
          { id: '6.1', name: 'Confidentiality',
            meaning: 'Degree to which the data the game keeps, such as personal details, settings, loadout and scores, is accessible only to those allowed to see it.',
            statements: [
              'The game does not collect or show personal or sensitive information (for example names, location, contacts, device identifiers) beyond what it needs.',
              'Any data the game saves (settings, loadout, scores) stays on the player\'s own device and is not sent anywhere else without the player knowing.'
            ] },
          { id: '6.2', name: 'Integrity',
            meaning: 'Degree to which the game prevents unauthorised access to, or changes of, its rules, score, timer and data.',
            statements: [
              'The player cannot use the game\'s controls to change the mission rules, score or timer to get an unearned result.',
              'The game rejects invalid input or state (for example registering the same resident twice) instead of corrupting its data.'
            ] },
          { id: '6.3', name: 'Non-repudiation',
            meaning: 'Degree to which important mission events, such as rescues, treatments and registrations, can be proven to have taken place.',
            statements: [
              'Important events (a resident rescued, treated or registered; a mission completed) are recorded so they cannot later be denied.',
              'The after-action report is a dependable record of what happened in the mission.'
            ] },
          { id: '6.4', name: 'Accountability',
            meaning: 'Degree to which the actions of the player can be traced uniquely to the results they produced in the game.',
            statements: [
              'The game can show which of the player\'s actions led to each result (rescues, first aid, registrations, hull strikes).',
              'A play session can be traced afterwards, for example from the report or from logs.'
            ] },
          { id: '6.5', name: 'Authenticity',
            meaning: 'Degree to which the game build and the mission results can be proven to be what they claim to be.',
            statements: [
              'I can be confident that the game build I received is genuine and has not been altered.',
              'The score and report shown are genuinely those of the mission just played.'
            ] },
          { id: '6.6', name: 'Resistance',
            meaning: 'Degree to which the game keeps operating properly when someone tries to attack it or tamper with its files, data or rules.',
            statements: [
              'The game kept working properly when someone tried to tamper with its files, saved data or mission rules, even on a rooted or jailbroken phone.',
              'Tampering with the game\'s settings or saved data did not stop the game from running or corrupt its record of missions.'
            ] }
        ]
      },
      {
        id: '7', name: 'Maintainability',
        blurb: 'Developers can understand, change and test the game easily.',
        subs: [
          { id: '7.1', name: 'Modularity',
            meaning: 'Degree to which the game is made of separate parts, such as boat, swimmer, residents, hazards, display, loadout and maps, so that a change to one part has minimal impact on the others.',
            statements: [
              'The game is built from separate parts (for example boat, player, residents, hazards, on-screen display, loadout, maps), so a change to one has little effect on the others.',
              'Each map (Manila, Barangay Mabuhay) is its own scene and data that can be changed without touching the core gameplay code.'
            ] },
          { id: '7.2', name: 'Reusability',
            meaning: 'Degree to which parts of the game, such as the rescue logic, boat, items and display, can be used in other missions or games.',
            statements: [
              'Parts such as the rescue logic, boat, items and on-screen display could be reused in other missions or games.',
              'The same mission logic runs on both maps without being rewritten.'
            ] },
          { id: '7.3', name: 'Analysability',
            meaning: 'Degree of effectiveness and efficiency with which a developer can assess the impact of a change, diagnose a defect and find the parts to modify.',
            statements: [
              'The code and documentation make it easy to understand what a feature does and where to find the cause of a defect.',
              'The project provides tools that help find problems (for example automated tests, logs, debug output).'
            ] },
          { id: '7.4', name: 'Modifiability',
            meaning: 'Degree to which new residents, items, hazards, maps and settings can be added or changed without introducing defects or lowering game quality.',
            statements: [
              'New residents, items, hazards or maps could be added or changed without breaking existing features.',
              'Values such as the mission time, scoring and item effects can be adjusted in settings or data, without editing core code.'
            ] },
          { id: '7.5', name: 'Testability',
            meaning: 'Degree of effectiveness and efficiency with which test criteria can be set for the game, and tests run by hand or automatically to check them.',
            statements: [
              'Individual features can be tested on their own (for example with automated tests or test scenes).',
              'Test criteria can be set and checked automatically (for example a script or bot that plays a whole mission).'
            ] }
        ]
      },
      {
        id: '8', name: 'Flexibility',
        blurb: 'The game can be adapted to new screens, systems and contexts, and moved or replaced with little effort.',
        subs: [
          { id: '8.1', name: 'Adaptability',
            meaning: 'Degree to which the game can be adapted to different phone screens, phone sizes, operating systems and graphics settings.',
            statements: [
              'The game adapts to different phone screens and sizes (resolution, aspect ratio, screen cut-outs, graphics quality) without code changes.',
              'The game could be adapted to other phone systems (Android and iOS) or to tablets with reasonable effort.'
            ] },
          { id: '8.2', name: 'Installability',
            meaning: 'Degree of effectiveness and efficiency with which the game can be installed and uninstalled in its target environment.',
            statements: [
              'The game could be installed and started easily, from the app store or an installer file, by following the instructions given.',
              'The game could be uninstalled completely, with no leftover files or effects on other apps.'
            ] },
          { id: '8.3', name: 'Replaceability',
            meaning: 'Degree to which a new version of the game, or other training material for the same purpose, can replace an older one without losing saved settings or data.',
            statements: [
              'A new version of the game can replace an older one as an app update, without the player losing settings or data.',
              'The game could be used in place of other flood-preparedness training materials (drills, videos, other games) for the same learning aim.'
            ] }
        ]
      },
      {
        id: '9', name: 'Safety',
        blurb: 'The game keeps players and residents out of unacceptable risk, and warns them of hazards in time.',
        subs: [
          { id: '9.1', name: 'Operational constraint',
            meaning: 'Degree to which the game keeps the boat and swimmer within safe operating limits when a hazard is encountered.',
            statements: [
              'When a hazard appeared, the game kept the boat and swimmer within safe limits, such as speed and reach.',
              'The safety limits the game applies are defined in one place, so they can be checked and changed safely.'
            ] },
          { id: '9.2', name: 'Risk identification',
            meaning: 'Degree to which the game identifies situations that could expose life, property or the environment to unacceptable risk.',
            statements: [
              'The game identifies situations that could expose residents or rescuers to serious risk, such as live wires or deep water.',
              'Risk situations are defined in the game data or code in a way that developers can review.'
            ] },
          { id: '9.3', name: 'Fail safe',
            meaning: 'Degree to which the game places the boat or swimmer in a safe state, or returns it to one, when a failure occurs.',
            statements: [
              'When a failure occurs (for example a stuck control or a hull strike), the game puts the boat or swimmer into a safe state.',
              'After a failure the game can revert to a safe condition from which the mission can continue or be restarted.'
            ] },
          { id: '9.4', name: 'Hazard warning',
            meaning: 'Degree to which the game warns players of unacceptable risks in time for them to react and keep the rescue safe.',
            statements: [
              'The game warned players about unacceptable risks, such as live wires and debris, early enough for them to react.',
              'Hazard warnings are triggered by the game logic, so they follow the actual state of the mission.'
            ] },
          { id: '9.5', name: 'Safe integration',
            meaning: 'Degree to which safe operation is kept when the boat, swimmer, residents, hazards and items interact in a mission.',
            statements: [
              'Safe operation was kept when the boat, swimmer, residents, hazards and items interacted during a mission.',
              'New items or hazards can be integrated with the existing rescue logic without making the game unsafe.'
            ] }
        ],
        comments: {
          title: 'Part B comments (optional)',
          prompt: 'Any technical concerns or recommendations (security, code structure, installation, phone platforms, safety)?'
        }
      }
    ]
  },
  {
    id: 'C',
    label: 'Part C',
    audience: 'everybody',
    title: 'Quality in use',
    intro: 'The statements below are about what you achieved and how it felt to use the game for its purpose: rescuing residents of a flooded barangay.',
    characteristics: [
      {
        id: 'Q1', name: 'Effectiveness',
        blurb: 'Players achieve the goals of the game accurately and completely. (Effectiveness has no sub-characteristics in ISO/IEC 25010.)',
        subs: [
          { id: 'Q1', name: 'Effectiveness',
            meaning: 'Accuracy and completeness with which players achieve the goals of the mission, such as reaching, rescuing, treating and registering residents.',
            statements: [
              'I was able to carry out the tasks the game asked of me (reach, rescue, treat, deliver and register residents).',
              'I reached the mission goals as completely and accurately as I intended.'
            ] }
        ],
        extras: 'missionResult'
      },
      {
        id: 'Q2', name: 'Efficiency',
        blurb: 'Achieving the goals takes a fitting amount of time and effort. (Efficiency has no sub-characteristics in ISO/IEC 25010.)',
        subs: [
          { id: 'Q2', name: 'Efficiency',
            meaning: 'Resources spent by players in relation to the accuracy and completeness with which they achieve the mission goals.',
            statements: [
              'I could complete the mission in a reasonable amount of time.',
              'The effort the mission demanded (taps, steps, attention) was in proportion to what I achieved.'
            ] }
        ]
      },
      {
        id: 'Q3', name: 'Satisfaction',
        blurb: 'Using the game is satisfying for the player.',
        subs: [
          { id: 'Q3.1', name: 'Usefulness',
            meaning: 'Degree to which the player is satisfied with the results of playing, such as understanding flood rescue and the value of preparing together.',
            statements: [
              'The game helped me understand what to do in a flood rescue (for example priorities, equipment, teamwork).',
              'Playing the game raised my awareness of flood risk and the value of preparing together (bayanihan).'
            ] },
          { id: 'Q3.2', name: 'Trust',
            meaning: 'Degree to which the player has confidence that the game will behave as intended, applying its rules and showing accurate information.',
            statements: [
              'I trusted the game to apply its rules and scoring consistently and fairly.',
              'I trusted the information shown (hazard warnings, resident status, timer) to be accurate.'
            ] },
          { id: 'Q3.3', name: 'Pleasure',
            meaning: 'Degree to which the player obtains pleasure from playing the game and from meeting personal needs such as enjoyment.',
            statements: [
              'I enjoyed playing the game.',
              'The mission held my interest and I would like to play it again or try the other map.'
            ] },
          { id: 'Q3.4', name: 'Comfort',
            meaning: 'Degree to which the player is physically comfortable while playing, including eyes, hands and balance.',
            statements: [
              'I felt physically comfortable while holding the phone and playing (no eye strain, sore thumbs or headache).',
              'The movement of the camera and the water did not cause me dizziness or discomfort.'
            ] }
        ]
      },
      {
        id: 'Q4', name: 'Freedom from risk',
        blurb: 'Using the game avoids harm to people, money and the environment.',
        subs: [
          { id: 'Q4.1', name: 'Economic risk mitigation',
            meaning: 'Degree to which playing the game limits the risk to the financial status of players, schools, barangays and their property.',
            statements: [
              'Using the game costs little or nothing, including data use, and needs no special equipment beyond an ordinary phone.',
              'A school, barangay or community group could adopt the game without a significant financial risk.'
            ] },
          { id: 'Q4.2', name: 'Health and safety risk mitigation',
            meaning: 'Degree to which playing the game limits the risk to the health and safety of people, and shows flood hazards in a way that supports safe behaviour.',
            statements: [
              'Playing the game did not put my health at risk (for example no flashing visuals, no pressure to play for long periods).',
              'The game shows flood hazards and people in danger respectfully and in a way that encourages safe behaviour in real life.'
            ] },
          { id: 'Q4.3', name: 'Environmental risk mitigation',
            meaning: 'Degree to which playing the game limits the risk to the environment, through energy and battery use and hardware needs, and promotes responsible behaviour.',
            statements: [
              'Running the game used a reasonable amount of battery and electricity and did not require buying a new phone.',
              'The game encourages environmentally responsible behaviour, for example about waste, drainage and flood preparedness.'
            ] }
        ]
      },
      {
        id: 'Q5', name: 'Context coverage',
        blurb: 'The game works in the situations it was made for, and beyond them.',
        subs: [
          { id: 'Q5.1', name: 'Context completeness',
            meaning: 'Degree to which the game can be used with effectiveness, efficiency, freedom from risk and satisfaction in all the specified contexts of use, such as home, school and on the go.',
            statements: [
              'The game was fully usable in the places and situations where I used it (for example at home, at school, or on the go).',
              'Whichever pack of items I chose, I could still complete rescues on the map I played.'
            ] },
          { id: 'Q5.2', name: 'Flexibility',
            meaning: 'Degree to which the game can be used with quality in contexts beyond those first specified, such as other rescue situations or classroom and community training.',
            statements: [
              'I could change my approach (map, item pack, route, boat or swimming) to suit different rescue situations and still succeed.',
              'The game could also serve other purposes and users, such as classroom lessons or community flood-preparedness training.'
            ] }
        ],
        comments: {
          title: 'Part C comments (optional)',
          prompt: 'What did you like best about the game, and what should we change first?'
        }
      }
    ]
  }
];

// ----- builder ---------------------------------------------------------------------------------------------------------
function createBayanihanForm() {
  const form = FormApp.create(FORM_TITLE);
  form.setDescription(introText_())
    .setConfirmationMessage(CONFIRMATION)
    .setProgressBar(true)
    .setCollectEmail(false)
    .setLimitOneResponsePerUser(false)
    .setAllowResponseEdits(false)
    .setShowLinkToRespondAgain(false);

  addConsentAndProfile_(form);

  // The branching question has to exist before the pages it points to, so it is created now (empty) and filled in last.
  let roleItem = null;
  let pageB = null;
  let pageC = null;

  QUESTIONNAIRE.forEach(function (part) {
    if (part.id === 'B') roleItem = addRoleQuestion_(form);    // end of Part A

    part.characteristics.forEach(function (c, index) {
      const page = form.addPageBreakItem()
        .setTitle(part.label + ' ' + c.name)
        .setHelpText(pageHelpText_(part, c, index));
      if (part.id === 'B' && index === 0) pageB = page;
      if (part.id === 'C' && index === 0) pageC = page;

      // Each sub-characteristic: a heading with its definition, then one star-rating question per statement.
      // The number and letter at the start of each question title keep the answers mapped to the standard in Sheets.
      c.subs.forEach(function (s) {
        form.addSectionHeaderItem()
          .setTitle(s.id + '  ' + s.name)
          .setHelpText(s.meaning);
        s.statements.forEach(function (statement, i) {
          form.addRatingItem()
            .setTitle(s.id + String.fromCharCode(97 + i) + '  ' + statement)
            .setRatingIcon(FormApp.RatingIconType.STAR)
            .setRatingScaleLevel(5)
            .setRequired(false);
        });
      });

      if (c.extras === 'missionResult') addMissionResultQuestions_(form);

      if (c.comments) {
        form.addParagraphTextItem().setTitle(c.comments.title).setHelpText(c.comments.prompt);
      }
    });
  });

  addOverallQuestions_(form);

  // Role question. Players and teachers should skip Part B, IT experts answer it. Apps Script cannot set that routing
  // (Forms rejects every navigation choice made from the API), so it is set in the form editor, as the log below says.
  roleItem.setChoiceValues([
    'Player, student or community member',
    'Teacher, trainer or disaster-risk (DRRM) practitioner',
    'Game developer, software engineer, tester or other IT professional'
  ]);

  Logger.log('Form created with %s items.', form.getItems().length);
  Logger.log('EDIT it here:   %s', form.getEditUrl());
  Logger.log('SEND this link: %s', form.getPublishedUrl());
  Logger.log('TO FINISH, in the editor set these two routings (question > "Go to section based on answer"):');
  Logger.log('  Consent: "I do not agree" > Submit form');
  Logger.log('  Your role: the first two choices > "' + pageC.getTitle() + '", the IT choice stays on Part B');
  return form.getPublishedUrl();
}

// The legend for the stars. It is shown at the top of every characteristic page and in the introduction.
const LEGEND = [
  'Stars, from left to right:',
  'Strongly Disagree',
  'Disagree',
  'Neutral',
  'Agree',
  'Strongly Agree'
].join('\n');

// Help text for a characteristic page. The first page of each part also carries the part's title, audience and intro.
function pageHelpText_(part, c, index) {
  return (index === 0 ? part.title + ' (' + part.audience + '). ' + part.intro + '\n\n' : '') +
    c.blurb + '\n\nRate each statement with five stars. Leave the stars empty if you cannot judge.\n\n' + LEGEND;
}

function introText_() {
  return [
    'Thank you for evaluating Bayanihan, a flood-rescue mobile phone game in which you pilot a rescue boat and a swimmer to bring ten residents of a flooded barangay to safety.',
    '',
    'This form measures the game against ISO/IEC 25010 (product quality, 2023 edition, and quality in use). Every rated item belongs to one sub-characteristic of the standard, shown with its number in the item title.',
    '',
    'Each statement is rated with five stars, using this legend. Leave the stars empty if you cannot judge.',
    '',
    LEGEND,
    '',
    'How long: about 15 minutes for players and teachers, about 30 minutes for IT experts (who also answer Part B).',
    '',
    'Before you start, please play at least one full mission on your phone, on one or both maps (Manila, Barangay Mabuhay). Your participation is voluntary and anonymous: we do not ask for your name or email, you can stop at any time, and your answers are used only to evaluate and improve the game.' +
      (CONTACT ? '\n\nQuestions? ' + CONTACT : '')
  ].join('\n');
}

function addConsentAndProfile_(form) {
  const consent = form.addMultipleChoiceItem();
  consent.setTitle('Consent')
    .setHelpText('I have read the description above, I am taking part voluntarily, and I understand that my answers are anonymous.')
    .setChoiceValues(['I agree to take part', 'I do not agree'])
    .setRequired(true);

  // The page break starts the "About you" page. "I do not agree" should end the form: that routing is set in the form editor,
  // because Apps Script cannot set any navigation choice (see the note in createBayanihanForm).
  form.addPageBreakItem().setTitle('About you').setHelpText('Anonymous. These answers help us interpret the ratings.');

  form.addMultipleChoiceItem().setTitle('Age group')
    .setChoiceValues(['Under 18', '18 to 24', '25 to 34', '35 to 49', '50 or older', 'Prefer not to say'])
    .setRequired(true);

  form.addMultipleChoiceItem().setTitle('How often do you play video games?')
    .setChoiceValues(['Rarely or never', 'A few times a month', 'Weekly', 'Daily'])
    .setRequired(true);

  form.addMultipleChoiceItem().setTitle('Have you experienced a flood, or taken part in flood-preparedness or rescue training?')
    .setChoiceValues(['I have experienced a flood', 'I have taken part in preparedness or rescue training', 'Both', 'Neither'])
    .setRequired(true);

  form.addCheckboxItem().setTitle('Which map(s) did you play?')
    .setChoiceValues(['Manila', 'Barangay Mabuhay (Carmona, Cavite)'])
    .setRequired(true);

  form.addMultipleChoiceItem().setTitle('About how many missions did you play?')
    .setChoiceValues(['1', '2 to 3', '4 to 5', '6 or more'])
    .setRequired(true);

  form.addMultipleChoiceItem().setTitle('Phone operating system you used')
    .setChoiceValues(['Android', 'iOS'])
    .showOtherOption(true)
    .setRequired(true);

  form.addParagraphTextItem().setTitle('Phone details (optional)')
    .setHelpText('For example: phone model, operating system version, memory, and the game build or date you received. This helps us read the performance answers.');
}

function addRoleQuestion_(form) {
  return form.addMultipleChoiceItem()
    .setTitle('Your role in this evaluation')
    .setHelpText('This decides what comes next. IT experts continue to Part B (technical qualities); everyone else goes straight to Part C.')
    .setRequired(true);
}

function addMissionResultQuestions_(form) {
  form.addMultipleChoiceItem()
    .setTitle('Did you finish a mission (all ten residents registered before time ran out)?')
    .setHelpText('Context for the Effectiveness ratings above.')
    .setChoiceValues(['Yes, all ten', 'Some, but not all', 'No', 'I did not play a full mission'])
    .setRequired(true);

  form.addScaleItem()
    .setTitle('In your best attempt, how many of the ten residents did you register at the Barangay Hall?')
    .setBounds(0, 10)
    .setLabels('None', 'All ten')
    .setRequired(false);
}

function addOverallQuestions_(form) {
  form.addPageBreakItem().setTitle('Overall')
    .setHelpText('Last questions, then submit.');

  form.addScaleItem()
    .setTitle('Overall, how would you rate the quality of Bayanihan as a software product?')
    .setBounds(1, 10)
    .setLabels('Very poor', 'Excellent')
    .setRequired(true);

  form.addScaleItem()
    .setTitle('How likely are you to recommend the game to a school, barangay or friend?')
    .setBounds(0, 10)
    .setLabels('Not at all likely', 'Extremely likely')
    .setRequired(true);

  form.addParagraphTextItem().setTitle('Anything else you would like to tell us? (optional)');
}
