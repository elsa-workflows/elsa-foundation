#!/usr/bin/env python3
"""Build and inspect real source hosts, owning every process and temporary store."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import json
import os
import re
from pathlib import Path
import shutil
import signal
import socket
import subprocess
import tempfile
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = {'desktop-light', 'desktop-dark', 'narrow-light', 'narrow-dark'}
CAPTURES = {
    'structured logs table accessibility': ['reference', 'logs-page', 'logs-selected', 'logs-empty', 'logs-dock'],
    'business decisions authoring and explanations': ['reference', 'decisions-list', 'decision-create', 'decision-table', 'decision-case-editor', 'decision-case-expectation', 'decision-cases', 'decision-regression', 'decision-published', 'decision-explanation', 'decision-clone', 'decision-conflict', 'decision-history', 'decisions-no-matches', 'decision-imported', 'decision-table-320', 'decision-table-320-scrolled'],
