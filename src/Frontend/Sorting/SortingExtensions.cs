﻿using BlazorTable;
using PrimeView.Frontend.Parameters;
using System;

namespace PrimeView.Frontend.Sorting
{
	public static class SortingExtensions
	{
		public static (string sortColumn, bool sortDescending) GetSortParameterValues<T>(this Table<T> table)
		{
			if (table == null)
				return (null, false);

			foreach (var column in table.Columns)
			{
				if (column.Field == null)
					continue;

				if (column.SortColumn)
					return (sortColumn: column.Field.GetPropertyParameterName(), sortDescending: column.SortDescending);
			}

			return (null, false);
		}

		public static bool SetSortParameterValues<T>(this Table<T> table, string sortColumn, bool sortDescending)
		{
			if (table == null || sortColumn == null)
				return false;

			foreach (var column in table.Columns)
			{
				if (column.Field == null || !column.Sortable)
					continue;

				if (column.Field.GetPropertyParameterName().EqualsIgnoreCaseOrNull(sortColumn))
				{
					column.SortColumn = true;
					column.SortDescending = sortDescending;

					return true;
				}
			}

			return false;
		}

		public static bool EqualsIgnoreCaseOrNull(this string x, string y)
		{
			return (x == null && y == null) || (x != null && y != null && x.Equals(y, StringComparison.OrdinalIgnoreCase));
		}
	}
}
