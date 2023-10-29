﻿using System.Reactive.Subjects;
using NetDaemon.HassModel.Entities;

namespace NetDaemon.HassModel.Tests.TestHelpers.HassClient;

internal sealed class HaContextMock : Mock<IHaContext>
{
    public HaContextMock()
    {
        Setup(m => m.StateAllChanges()).Returns(StateAllChangeSubject);
        Setup(m => m.Events).Returns(EventsSubject);
    }

    public Subject<StateChange> StateAllChangeSubject { get; } = new();
    public Subject<Event> EventsSubject { get; } = new();
}
