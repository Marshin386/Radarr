import { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import { fetchNamingSettings } from 'Store/Actions/settingsActions';
import createRootFoldersSelector from 'Store/Selectors/createRootFoldersSelector';
import translate from 'Utilities/String/translate';

const rootFoldersSelector = createRootFoldersSelector();

export type FolderLayout = 'movieFolder' | 'flat' | 'custom';

export function getDisplayedMovieFolder(
  layout: FolderLayout | undefined,
  movieFolder?: string,
  customFolder?: string
) {
  if (layout === 'flat') {
    return undefined;
  }

  if (layout === 'custom') {
    return customFolder || undefined;
  }

  return movieFolder || undefined;
}

export function getAddMovieRootFolderHelpText(
  layout: FolderLayout | undefined,
  movieFolder?: string,
  customFolder?: string,
  customFormat?: string
) {
  if (layout === 'flat') {
    return translate('AddNewMovieRootFolderHelpTextFlat');
  }

  if (layout === 'custom') {
    if (customFolder) {
      return translate('AddNewMovieRootFolderHelpTextCustom', {
        folder: customFolder,
      });
    }

    // An empty string was resolved and produced no directory.
    if (customFolder === '') {
      return translate('AddNewMovieRootFolderHelpTextCustomEmpty');
    }

    if (customFormat) {
      return translate('AddNewMovieRootFolderHelpTextCustomPattern', {
        format: customFormat,
      });
    }

    return translate('AddNewMovieRootFolderHelpTextCustomEmpty');
  }

  return translate('AddNewMovieRootFolderHelpText', {
    folder: movieFolder ?? '',
  });
}

export default function useAddMovieRootFolderHint(
  rootFolderPath: string | undefined,
  movieFolder?: string,
  customFolder?: string
) {
  const dispatch = useDispatch();
  const rootFolders = useSelector(rootFoldersSelector);
  const naming = useSelector((state: AppState) => state.settings.naming);
  const layout = rootFolders.items.find(
    (rootFolder) => rootFolder.path === rootFolderPath
  )?.folderLayout;
  const customFormat = naming.item?.customMovieFolderFormat;

  useEffect(() => {
    if (!naming.isPopulated && !naming.isFetching && !naming.error) {
      dispatch(fetchNamingSettings());
    }
  }, [dispatch, naming.error, naming.isFetching, naming.isPopulated]);

  return {
    displayedFolder: getDisplayedMovieFolder(layout, movieFolder, customFolder),
    helpText: getAddMovieRootFolderHelpText(
      layout,
      movieFolder,
      customFolder,
      customFormat
    ),
  };
}
