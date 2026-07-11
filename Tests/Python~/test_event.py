from lliquidlink.client import Event


def test_single_handler_receives_args():
    received = []
    event = Event()
    event += lambda *a, **kw: received.append((a, kw))

    event(1, 2, foo="bar")

    assert received == [((1, 2), {"foo": "bar"})]


def test_multiple_handlers_invoked_in_registration_order():
    order = []
    event = Event()
    event += lambda: order.append("first")
    event += lambda: order.append("second")

    event()

    assert order == ["first", "second"]


def test_isub_removes_handler():
    order = []
    event = Event()

    def first():
        order.append("first")

    def second():
        order.append("second")

    event += first
    event += second
    event -= first

    event()

    assert order == ["second"]


def test_isub_missing_handler_is_noop():
    event = Event()

    def handler():
        pass

    event -= handler  # must not raise, mirrors C# delegate -= semantics
    event()
