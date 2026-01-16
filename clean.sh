#!/bin/bash
# Remove all Syncthing conflict files recursively

find . -name "*.sync-conflict-*" -type f -delete -print
