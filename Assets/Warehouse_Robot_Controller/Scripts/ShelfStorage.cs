using UnityEngine;

namespace Warcool.WarehouseRobot
{
    public class ShelfStorage : MonoBehaviour
    {
        [System.Serializable]
        public class StorageColumn
        {
            public Transform[] levels;
        }

        [Header("Storage Points")]
        public StorageColumn[] columns;

        private CargoBox[,] boxes;
        private int maxLevels;

        private void Awake()
        {
            InitializeStorage();
            AutoFindExistingBoxes();
        }

        private void InitializeStorage()
        {
            maxLevels = 0;

            if (columns == null)
            {
                boxes = new CargoBox[0, 0];
                return;
            }

            for (int i = 0; i < columns.Length; i++)
            {
                if (columns[i] != null && columns[i].levels != null)
                    maxLevels = Mathf.Max(maxLevels, columns[i].levels.Length);
            }

            boxes = new CargoBox[columns.Length, maxLevels];
        }

        private void AutoFindExistingBoxes()
        {
            if (columns == null || boxes == null)
                return;

            for (int column = 0; column < columns.Length; column++)
            {
                int levelsCount = GetLevelsCount(column);

                for (int level = 0; level < levelsCount; level++)
                {
                    Transform point = GetPoint(column, level);

                    if (point == null)
                        continue;

                    CargoBox box = point.GetComponentInChildren<CargoBox>();

                    if (box != null)
                    {
                        boxes[column, level] = box;
                        box.isCarried = false;
                    }
                }
            }
        }

        public bool HasBoxToTake(int column, out int level, out CargoBox box)
        {
            level = -1;
            box = null;

            if (!IsValidColumn(column))
                return false;

            for (int i = GetLevelsCount(column) - 1; i >= 0; i--)
            {
                if (boxes[column, i] != null)
                {
                    level = i;
                    box = boxes[column, i];
                    return true;
                }
            }

            return false;
        }

        public bool HasFreePlaceToPut(int column, out int level)
        {
            level = -1;

            if (!IsValidColumn(column))
                return false;

            for (int i = 0; i < GetLevelsCount(column); i++)
            {
                if (boxes[column, i] == null)
                {
                    level = i;
                    return true;
                }
            }

            return false;
        }

        public void RemoveBox(int column, int level)
        {
            if (!IsValidCell(column, level))
                return;

            boxes[column, level] = null;
        }

        public void PutBox(int column, int level, CargoBox box)
        {
            if (box == null)
                return;

            PutBox(column, level, box, box.transform.rotation);
        }

        public void PutBox(int column, int level, CargoBox box, Quaternion worldRotation)
        {
            if (!IsValidCell(column, level) || box == null)
                return;

            Transform point = GetPoint(column, level);

            if (point == null)
                return;

            boxes[column, level] = box;

            box.transform.SetParent(point, true);
            box.transform.position = point.position;
            box.transform.rotation = worldRotation;

            box.isCarried = false;
        }

        public Transform GetPoint(int column, int level)
        {
            if (!IsValidCell(column, level))
                return null;

            return columns[column].levels[level];
        }

        public CargoBox GetBox(int column, int level)
        {
            if (!IsValidCell(column, level))
                return null;

            return boxes[column, level];
        }

        private int GetLevelsCount(int column)
        {
            if (!IsValidColumn(column))
                return 0;

            if (columns[column].levels == null)
                return 0;

            return columns[column].levels.Length;
        }

        private bool IsValidColumn(int column)
        {
            return columns != null &&
                   boxes != null &&
                   column >= 0 &&
                   column < columns.Length &&
                   columns[column] != null;
        }

        private bool IsValidCell(int column, int level)
        {
            return IsValidColumn(column) &&
                   columns[column].levels != null &&
                   level >= 0 &&
                   level < columns[column].levels.Length &&
                   level < maxLevels;
        }
    }
}
