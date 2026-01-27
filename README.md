# SwarmDroneUnity

Physics-Based Probabilistic Swarm Drone Simulation for Indoor Object Search

## Overview

This repository contains the source code and simulation framework used in the
journal article:

**“Probabilistic Modeling and Experimental Validation of Swarm Drones for Object Search Missions under Dynamic Performance Variations”**

The project implements a physics-based indoor swarm drone simulation using a
leader–slave coordination structure and probabilistic decision-making under
mechanical feasibility constraints. The simulation is designed to evaluate how
decision robustness interacts with quadrotor dynamics, collision avoidance, and
environmental uncertainty in confined indoor spaces.

All results reported in the paper are generated using this repository.

---

## Key Features

- Physics-based indoor quadrotor simulation (Unity engine)
- Leader–slave swarm coordination (fixed roles)
- Probabilistic belief-based target search
- Monte Carlo rollout decision evaluation
- Mechanical feasibility constraints:
  - Thrust limits
  - Rotor speed bounds
  - Rigid-body dynamics
- Collision-aware motion execution
- Large-scale Monte Carlo experimentation (N = 1000)

---

## System Architecture

- **Leader drone**
  - Belief aggregation
  - Global coordination
  - Mission supervision

- **Slave drones**
  - Local exploration
  - Sensing and target detection
  - Physically constrained navigation

Decision-making is explicitly filtered through mechanical constraints to ensure
all executed actions remain physically realizable.

---

## Simulation Environment

The indoor environment consists of:
- A central home base
- Multiple enclosed rooms
- Narrow passages and wall boundaries

This layout is intentionally designed to introduce:
- Partial observability
- Limited maneuvering space
- Elevated collision risk

The environment geometry mirrors the configuration used in the paper.

---

## Repository Structure

```text
SwarmDroneUnity/
├── Assets/
│   ├── Scripts/          # Decision-making, swarm logic, dynamics
│   ├── Scenes/           # Indoor environments and simulation scenes
│   ├── Prefabs/          # Drone and environment prefabs
│   └── Materials/        # Visual and physical materials
├── ProjectSettings/      # Unity project configuration
├── Packages/             # Unity package dependencies
├── README.md             # Project documentation