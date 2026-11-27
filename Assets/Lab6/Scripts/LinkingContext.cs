// =================================================================================================
//  Lab 6 - From lobby to multiplayer game: LINKING CONTEXT
//  XJO - Xarxes per a Jocs Online
// =================================================================================================
//
//  THE EXERCISE
//    A network ID is a number that means the same object on every machine (slides 12 to 14).
//    The linking context is the table, network ID <-> GameObject, one per machine (slide 15).
//    The server creates the IDs. The client uses the ones that come in the messages.
//
//  HOW TO RUN IT
//    Open the scene S_LinkingTest and press Play. Each test says PASS, FAIL or SKIPPED.
//
//  WHAT YOU HAVE TO DO
//    TODO 1 to 3 below, in order. In each one, delete the Lab6Todo.NotDone(...) line and write yours.
//    Stuck? Disable the GameObject "LinkingTest", enable "LinkingTestSolution", and compare.
//    Its code is in LinkingContextSolution.cs.
//
//  THEN, IN YOUR GAME
//    Copy this file into your Deliverable 3 project. One LinkingContext on the server, one on each
//    client. Replace your objectId with network IDs (slide 22).
//
// =================================================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

public class LinkingContext
{
    readonly Dictionary<int, GameObject> m_objects = new Dictionary<int, GameObject>();
    int m_nextId = 1;                              // 0 means "no ID"

    // How many objects have a network ID right now. For the debug overlay (slide 19).
    public int Count { get { return m_objects.Count; } }
    public IEnumerable<KeyValuePair<int, GameObject>> Objects { get { return m_objects; } }

    // --------------------------------------------------------------------------------- TODO 1 ---
    //  Goal: register an object, in two versions.
    //  On the server, give 'go' the next free ID, store it, return the ID. Never reuse an ID (slide 14).
    //  On the client, store 'go' under the ID the server sent.
    //  Look it up: Dictionary<TKey, TValue> indexer in the docs. m_nextId++ returns the old value.
    //  Test scene when it works:  two objects called "Cube" get IDs 1 and 2
    public int Register(GameObject go)             // server, creates a new ID
    {
        int id = m_nextId++;
        m_objects[id] = go;
        return id;
    }

    public void Register(GameObject go, int id)    // client, uses the ID it was given
    {
        m_objects[id] = go;
    }

    // --------------------------------------------------------------------------------- TODO 2 ---
    //  Goal: the object with this ID, or null if there is none.
    //  With UDP a message can arrive after the object was destroyed. That is normal. Return null,
    //  never throw (slide 15).
    //  Look it up: Dictionary.TryGetValue in the docs.
    //  Test scene when it works:  Find(99), unknown ID -> null
    public GameObject Find(int id)                 // both, null if unknown
    {
        GameObject go;
        return m_objects.TryGetValue(id, out go)
        ? go : null;
    }

    // --------------------------------------------------------------------------------- TODO 3 ---
    //  Goal: forget this ID, when its object is destroyed. An unknown ID is not an error.
    //  Look it up: Dictionary.Remove in the docs.
    //  Test scene when it works:  Find(1) after Unregister(1) -> null
    public void Unregister(int id)                 // both, when the object is destroyed
    {
        m_objects.Remove(id);
    }
}

// Thrown by a TODO you have not done yet. The test scene catches it and reports SKIPPED.
// Not called Todo, so it does not clash with the Lab 3 handout if both are in your project.
public class Lab6TodoException : Exception
{
    public readonly int number;
    public Lab6TodoException(int number, string detail) : base(detail) { this.number = number; }
}

public static class Lab6Todo
{
    public static void NotDone(int number, string detail) { throw new Lab6TodoException(number, detail); }
}
