LAB 6 - FROM LOBBY TO MULTIPLAYER GAME
XJO - Xarxes per a Jocs Online

WHAT IS IN THIS HANDOUT
  Scenes/S_LinkingTest                  a test scene. No game, no lobby, works on its own
  Scripts/LinkingContext.cs             network ID <-> GameObject (slide 15). TODO 1 to 3
  Scripts/NetSim.cs                     latency, jitter and loss on purpose (slide 21). TODO 4 and 5
  Scripts/NetDebugOverlay.cs            see your network (slide 19). Nothing to do, just use it
  Scripts/LinkingTest.cs                checks your TODOs
  Scripts/LinkingContextSolution.cs     the finished LinkingContext
  Scripts/NetSimSolution.cs             the finished NetSim

IN CLASS, PART 1 (slide 22)
  Open S_LinkingTest and press Play. Every check says PASS, FAIL or SKIPPED.
  Do TODO 1 to 3 in LinkingContext.cs until the linking checks say PASS.
  Stuck? Disable the GameObject LinkingTest, enable LinkingTestSolution,
  press Play and compare. The code is in LinkingContextSolution.cs.
  Then copy LinkingContext.cs into your Deliverable 3 project and replace your objectId
  with network IDs. The server registers, the client uses the ID it receives.

IN CLASS, PART 2 (slide 23)
  Do TODO 4 and 5 in NetSim.cs until the NetSim checks say PASS.
  Copy NetSim.cs and NetDebugOverlay.cs into your game, send through NetSim on both sides,
  add three sliders, and play with 200 ms of latency, then with 10 % loss.
  Write down what breaks. It is your starting point for Labs 8 and 9.

NOTHING TO HAND IN
  World state replication (Deliverable 4, 10 %) is presented in Lab 7 and starts from this code.
