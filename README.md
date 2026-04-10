# NanoAgent

(2m 25s · ↓ 2.5k tokens)
* .… (5m 3s · ↓ 6.4k tokens · thinking)

 Write(src\app\models\task.model.ts)
  ⎿  Wrote 13 lines to src\app\models\task.model.ts
       1 export type TaskStatus = 'TODO' | 'IN_PROGRESS' | 'DONE';
       2
       3 /**
       4  * Defines the immutable structure for a task managed by the application state.
       5  */
       6 export interface Task {
       7   id: string; // Unique ID (e.g., UUID or sequential local ID)
       8   title: string;
       9   description?: string; // Optional description field
      10   status: TaskStatus;
     … +3 lines