# SpaceBattle.Simulations experimental notice

This package contains the five independent native SpaceBattle simulations.

SpaceBattle matches tick by tick within the verified desktop and browser runs. A shared bit-exact baseline across runtimes is still open. The isolated Unity CPU and GPU replay matched through tick 1,200, and its first difference from the browser reference occurred at tick 1. Keep replay oracles specific to the runtime that produced them.
