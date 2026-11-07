"""Offline manifest controls. No process, network, VM or engine is started."""
import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("topology", Path(__file__).with_name("topology.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
validate = module.validate


def fixture(count=4, placement="DatabaseHost"):
    run = "efficiency-a1b2c3d4"

    def host(number):
        return {
            "machineId": f"{number:032x}",
            "resourceId": "/subscriptions/11111111-2222-3333-4444-555555555555/"
                          f"resourceGroups/rg-{run}/providers/Microsoft.Compute/virtualMachines/node-{number}",
            "privateAddress": f"10.193.253.{number}",
            "sshHostKeySha256": "SHA256:" + "A" * 43,
            "sshUser": "elsaeff",
            "taskRoot": f"/home/elsaeff/{run}",
            "port": 5095,
        }

    data = {
        "schemaVersion": 2,
        "runId": run,
        "nodeCount": count,
        "driverPlacement": placement,
        "database": host(4),
        "workers": [host(number) for number in range(5, 5 + count)],
        "sourceCommit": "a" * 40,
        "sourceTree": "b" * 40,
        "binarySha256": {"Elsa.Workbench.dll": "c" * 64,
                         "Elsa.Workflows.Runtime.dll": "d" * 64},
    }
    if placement == "SeparateHost":
        data["driver"] = host(9)
    return data


class TopologyControls(unittest.TestCase):
    def test_two_and_four_workers_accept_explicit_database_cohost(self):
        for count in (2, 4):
            with self.subTest(count=count):
                result = validate(fixture(count))
                self.assertEqual(count + 1, result["declaredIndependentHosts"])
                self.assertEqual("DatabaseHost", result["driverPlacement"])
                self.assertIn("need live corroboration", result["boundary"])

    def test_independent_driver_is_an_explicit_optional_boundary(self):
        for count in (2, 4):
            with self.subTest(count=count):
                self.assertEqual(count + 2, validate(fixture(count, "SeparateHost"))["declaredIndependentHosts"])

    def test_duplicate_worker_machine_resource_or_address_is_rejected(self):
        for key in ("machineId", "resourceId", "privateAddress"):
            with self.subTest(key=key):
                data = fixture()
                data["workers"][1][key] = data["workers"][0][key]
                with self.assertRaises(ValueError):
                    validate(data)

    def test_resource_casing_does_not_hide_one_vm_as_two_workers(self):
        data = fixture()
        data["workers"][1]["resourceId"] = data["workers"][0]["resourceId"].upper()
        with self.assertRaises(ValueError):
            validate(data)

    def test_database_and_worker_are_distinct_even_with_cohosted_driver(self):
        data = fixture()
        data["database"] = copy.deepcopy(data["workers"][0])
        with self.assertRaises(ValueError):
            validate(data)

    def test_separate_driver_cannot_alias_database(self):
        data = fixture(4, "SeparateHost")
        data["driver"] = copy.deepcopy(data["database"])
        with self.assertRaises(ValueError):
            validate(data)

    def test_cohosted_driver_does_not_silently_ignore_a_second_host_record(self):
        data = fixture()
        data["driver"] = copy.deepcopy(data["database"])
        with self.assertRaises(ValueError):
            validate(data)

    def test_missing_driver_boundary_is_rejected(self):
        data = fixture()
        del data["driverPlacement"]
        with self.assertRaises(ValueError):
            validate(data)

    def test_extra_worker_or_noninteger_count_is_rejected(self):
        for count in (3, 2.0, True):
            with self.subTest(count=count):
                data = fixture(2)
                data["nodeCount"] = count
                with self.assertRaises(ValueError):
                    validate(data)

    def test_nonfixture_addresses_are_rejected(self):
        for address in ("127.0.0.1", "0.0.0.0", "169.254.1.1", "8.8.8.8", "::1", "fd00::1"):
            with self.subTest(address=address):
                data = fixture()
                data["workers"][0]["privateAddress"] = address
                with self.assertRaises(ValueError):
                    validate(data)

    def test_stale_or_unnormalized_roots_are_rejected(self):
        for root in ("/home/elsaeff/efficiency-historical", "/home/elsaeff/../efficiency-a1b2c3d4",
                     "/home//elsaeff/efficiency-a1b2c3d4", "/", "efficiency-a1b2c3d4"):
            with self.subTest(root=root):
                data = fixture()
                data["workers"][0]["taskRoot"] = root
                with self.assertRaises(ValueError):
                    validate(data)

    def test_missing_runtime_hash_or_truncated_fingerprint_is_rejected(self):
        data = fixture()
        del data["binarySha256"]["Elsa.Workflows.Runtime.dll"]
        with self.assertRaises(ValueError):
            validate(data)
        data = fixture()
        data["workers"][0]["sshHostKeySha256"] = "SHA256:unchecked"
        with self.assertRaises(ValueError):
            validate(data)

    def test_boolean_worker_port_is_rejected(self):
        data = fixture()
        data["workers"][0]["port"] = True
        with self.assertRaises(ValueError):
            validate(data)


if __name__ == "__main__":
    unittest.main(verbosity=2)
