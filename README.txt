ARDEN'S THEOREM SOLVER - native Windows desktop app
(no browser, no Python, no downloads)

1. Extract this whole ZIP to a folder (e.g. Documents\ArdensSolver).
2. Double-click  Build_ArdensSolver_EXE.bat
   It compiles ArdensSolver.cs with the compiler built into Windows (takes ~5 seconds),
   creates ArdensSolver.exe + a Desktop shortcut, and opens the app.
3. From then on just use the Desktop shortcut (or ArdensSolver.exe).

If Windows SmartScreen warns about the new exe: More info > Run anyway (you built it yourself).
If the build prints errors, send them to me.

WHAT'S NEW IN THIS VERSION
  * Colour everywhere: gradient banner, colour-coded tabs, coloured regex
    (each alphabet symbol has its own colour, * red, + amber, states blue),
    coloured solution steps.
  * State diagram coloured by role:  green = start, gold inner ring = accepting,
    red = dead/trap state, grey dashed = unreachable, blue = normal.
    Edges take the colour of their symbol; a legend is drawn on the diagram.
  * NEW "Properties" tab: structure (reachable / unreachable / dead states,
    self-loops), language (empty / finite / infinite, shortest + longest accepted
    string, first accepted strings, count of accepted strings per length with bars),
    minimality (equivalent states via partition refinement), graph info
    (strongly connected components, per-state in/out degree table) and regex
    statistics (symbols, stars, unions, star height).
  * NEW "Test a string" bar: type a string and see ACCEPTED / REJECTED plus the
    exact path it takes, highlighted in orange on the State diagram tab.
  * NEW buttons: "Minimize DFA" (loads the minimal equivalent machine),
    "Random DFA" (random transitions for the current states/alphabet),
    "Save report..." (equations, steps, result and properties to a .txt file).
  * 5 new examples: contains 101, even 0s AND even 1s, a machine with a trap state,
    a 3-letter alphabet, and a non-minimal machine with an unreachable state.

Optional: the python\ folder has the original Tkinter version (Mac/Linux) - it never opens a browser.
It does not include the new features above.

ICON: ArdensSolver.ico is used automatically for the .exe, the Desktop shortcut and the window.
