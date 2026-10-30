"""Agents discover real Python contracts through the shipped offline CLI."""
import json
import os
import subprocess
import sys
import tempfile
import unittest


class HelperDiscoveryScenarios(unittest.TestCase):
    def invoke(self, *arguments):
        with tempfile.TemporaryDirectory() as directory:
            return subprocess.run([sys.executable, "-m", "gta_use", "helpers", *arguments],
                env=dict(os.environ, GTA_USE_DISCOVERY=directory),
                capture_output=True, text=True, timeout=10)

    def test_agent_can_discover_navigation_parameters_without_a_running_game(self):
        listed = self.invoke()
        self.assertEqual(0, listed.returncode, listed.stderr)
        names = [item["name"] for item in json.loads(listed.stdout)["helpers"]]
        self.assertIn("navigation.navigate_to", names)
        self.assertIn("combat.fire_from_vehicle", names)
        self.assertTrue(all("._" not in name for name in names))
        described = self.invoke("NAVIGATE_TO")
        self.assertEqual(0, described.returncode, described.stderr)
        helper, = json.loads(described.stdout)["helpers"]
        self.assertEqual("from gta_use.navigation import navigate_to", helper["importStatement"])
        self.assertIn("move_blend_ratio=", helper["signature"])
        self.assertIn("radius=", helper["signature"])
        self.assertNotIn("arrival_radius=", helper["signature"])
        self.assertIn("parkour", helper["signature"])
        self.assertTrue(helper["description"])

    def test_unknown_helper_is_rejected_with_offline_recovery_guidance(self):
        failed = self.invoke("teleport_everything")
        self.assertEqual(2, failed.returncode)
        self.assertIn("No SDK helper matches", failed.stderr)
        self.assertIn("helpers to list names", failed.stderr)
        self.assertNotIn("Traceback", failed.stderr)

    def test_module_query_explains_vehicle_fire_effect_limits(self):
        result = self.invoke("combat")
        self.assertEqual(0, result.returncode, result.stderr)
        helpers = json.loads(result.stdout)["helpers"]
        self.assertTrue(all(item["name"].startswith("combat.") for item in helpers))
        fire = next(item for item in helpers if item["name"] == "combat.fire_from_vehicle")
        self.assertIn("ammoSpent", fire["description"])
        self.assertIn("damaged", fire["description"])
