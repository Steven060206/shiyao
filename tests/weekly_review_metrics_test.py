import unittest

from tools.weekly_review_metrics import summarize


def row(number, label, route, *events):
    return {"id": number, "label": label, "route": route, "events": events}


class WeeklyReviewMetricsTests(unittest.TestCase):
    def test_reposts_and_separate_obligations_in_one_original(self):
        stats = summarize([
            row(1, "A", "pending", "report", "form"), row(2, "A", "pending", "report"),
            row(3, "A", "task", "report"), row(4, "N", "pending"), row(5, "U", "pending"),
        ])
        self.assertEqual(stats["required_events"], 2)
        self.assertEqual(stats["surfaced_events"], 2)
        self.assertEqual(stats["automatic_events"], 1)
        self.assertEqual(stats["pending_events"], 2)
        self.assertEqual(stats["pending_per_useful_event"], 2)
        self.assertEqual(stats["pending_only_events"], 1)
        self.assertEqual(stats["pending_per_additional_event"], 4)
        self.assertEqual(stats["pending_labels"], {"A": 2, "N": 1, "U": 1})

    def test_notice_only_unknown_content_and_no_useful_pending_denominator(self):
        stats = summarize([
            row(1, "A", "notice", "form"), row(2, "A", "ignored", "exam"),
            row(3, "X", "ignored"), row(4, "I", "pending"), row(5, "A", "change", "room"),
        ])
        self.assertEqual(stats["missing_events"], ["exam", "form"])
        self.assertEqual(stats["notice_only_events"], ["form"])
        self.assertIsNone(stats["pending_per_useful_event"])
        self.assertIsNone(stats["pending_per_additional_event"])
        self.assertEqual(stats["labels"]["X"], 1)

    def test_pending_repost_of_an_automatic_task_adds_no_new_obligation(self):
        stats = summarize([
            row(1, "A", "task", "assignment"),
            row(2, "A", "pending", "assignment"),
            row(3, "I", "pending"),
        ])
        self.assertEqual(stats["pending_per_useful_event"], 2)
        self.assertEqual(stats["pending_only_events"], 0)
        self.assertIsNone(stats["pending_per_additional_event"])

    def test_duplicate_original_and_invalid_reference_label_are_rejected(self):
        with self.assertRaises(ValueError):
            summarize([row(1, "N", "ignored"), row(1, "N", "ignored")])
        for bad in [row(1, "A", "ignored"), row(1, "U", "pending", "unproven"), row(1, "N", "unknown")]:
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                summarize([bad])
