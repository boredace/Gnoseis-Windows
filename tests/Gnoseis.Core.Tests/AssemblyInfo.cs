/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

// Releasing database connections clears every SQLite connection pool in the process (as the app does
// when it restores a backup or sets a file aside, and as each test database does when it is deleted).
// Test classes running at the same time would lose connections they are using, so they run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
