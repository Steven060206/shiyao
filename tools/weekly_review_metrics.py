"""Measure task discoverability and pending-list workload against independent event labels."""

from collections import Counter


def summarize(records):
    """Each record represents one original message and its final route in a replay.

    Labels cover definite action (A), uncertain applicability (U), information or
    voluntary activity (I), chatter or receipts (N), and insufficient material (X).
    Event IDs identify independently established obligations, shared across reminders
    and reposts. A task, change or pending entry surfaces the attached original.
    """
    origins = set()
    required = set()
    automatic = set()
    pending = set()
    notices = set()
    routes = Counter()
    pending_labels = Counter()
    labels = Counter()
    for record in records:
        origin, label, route = record["id"], record["label"], record["route"]
        events = set(record["events"])
        if origin in origins:
            raise ValueError("duplicate original message")
        if label not in {"A", "U", "I", "N", "X"} or (label == "A") != bool(events):
            raise ValueError("only definite action messages carry confirmed event IDs")
        if route not in {"pending", "task", "change", "notice", "ignored"}:
            raise ValueError("unknown final route")
        origins.add(origin)
        labels[label] += 1
        routes[route] += 1
        required.update(events)
        if route == "pending":
            pending_labels[label] += 1
            pending.update(events)
        elif route in {"task", "change"}:
            automatic.update(events)
        elif route == "notice":
            notices.update(events)
    surfaced = pending | automatic
    only_in_pending = pending - automatic
    return {
        "messages": len(origins),
        "labels": dict(labels),
        "routes": dict(routes),
        "required_events": len(required),
        "surfaced_events": len(surfaced),
        "automatic_events": len(automatic),
        "pending_events": len(pending),
        "pending_only_events": len(only_in_pending),
        "missing_events": sorted(required - surfaced),
        "notice_only_events": sorted(notices - surfaced),
        "pending_labels": dict(pending_labels),
        "pending_per_useful_event": routes["pending"] / len(pending) if pending else None,
        "pending_per_additional_event": (
            routes["pending"] / len(only_in_pending) if only_in_pending else None
        ),
    }
