import React, { useCallback, useState } from 'react';
import { useDispatch } from 'react-redux';
import FormInputGroup from 'Components/Form/FormInputGroup';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { icons, inputTypes, kinds } from 'Helpers/Props';
import {
  deleteRootFolder,
  setRootFolderValue,
} from 'Store/Actions/rootFolderActions';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './RootFolderRow.css';

interface RootFolderRowProps {
  id: number;
  path: string;
  folderLayout?: string;
  pendingLayout?: string;
  accessible: boolean;
  freeSpace?: number;
  unmappedFolders: object[];
}

function RootFolderRow(props: RootFolderRowProps) {
  const {
    id,
    path,
    folderLayout,
    pendingLayout,
    accessible,
    freeSpace = 0,
    unmappedFolders = [],
  } = props;

  const isUnavailable = !accessible;

  const dispatch = useDispatch();

  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [proposedLayout, setProposedLayout] = useState<string | null>(null);
  const savedLayout = folderLayout || 'movieFolder';
  const selectedLayout = pendingLayout || savedLayout;

  const layoutOptions = [
    {
      key: 'movieFolder',
      value: translate('MovieFolderLayoutMovieFolder'),
    },
    { key: 'flat', value: translate('MovieFolderLayoutFlat') },
    { key: 'custom', value: translate('MovieFolderLayoutCustom') },
  ];

  const onDeletePress = useCallback(() => {
    setIsDeleteModalOpen(true);
  }, [setIsDeleteModalOpen]);

  const onDeleteModalClose = useCallback(() => {
    setIsDeleteModalOpen(false);
  }, [setIsDeleteModalOpen]);

  const onConfirmDelete = useCallback(() => {
    dispatch(deleteRootFolder({ id }));

    setIsDeleteModalOpen(false);
  }, [dispatch, id]);

  const onLayoutChange = useCallback(
    ({ value }: { value: string }) => {
      if (value === selectedLayout) {
        return;
      }

      if (value === savedLayout) {
        dispatch(setRootFolderValue({ id, folderLayout: value }));
        return;
      }

      setProposedLayout(value);
    },
    [dispatch, id, savedLayout, selectedLayout]
  );

  const onLayoutChangeCancel = useCallback(() => {
    setProposedLayout(null);
  }, []);

  const onLayoutChangeConfirm = useCallback(() => {
    if (proposedLayout) {
      dispatch(setRootFolderValue({ id, folderLayout: proposedLayout }));
    }

    setProposedLayout(null);
  }, [dispatch, id, proposedLayout]);

  return (
    <TableRow>
      <TableRowCell>
        {isUnavailable ? (
          <div className={styles.unavailablePath}>
            {path}

            <Label className={styles.unavailableLabel} kind={kinds.DANGER}>
              {translate('Unavailable')}
            </Label>
          </div>
        ) : (
          <Link className={styles.link} to={`/add/import/${id}`}>
            {path}
          </Link>
        )}
      </TableRowCell>

      <TableRowCell className={styles.freeSpace}>
        {isUnavailable || isNaN(Number(freeSpace))
          ? '-'
          : formatBytes(freeSpace)}
      </TableRowCell>

      <TableRowCell className={styles.layout}>
        <FormInputGroup
          type={inputTypes.SELECT}
          name="folderLayout"
          value={selectedLayout}
          values={layoutOptions}
          onChange={onLayoutChange}
        />
      </TableRowCell>

      <TableRowCell className={styles.unmappedFolders}>
        {isUnavailable ? '-' : unmappedFolders.length}
      </TableRowCell>

      <TableRowCell className={styles.actions}>
        <IconButton
          title={translate('RemoveRootFolder')}
          name={icons.REMOVE}
          onPress={onDeletePress}
        />
      </TableRowCell>

      <ConfirmModal
        isOpen={proposedLayout !== null}
        kind={kinds.WARNING}
        title={translate('MovieFolderLayoutChange')}
        message={translate('MovieFolderLayoutChangeMessage', { path })}
        confirmLabel={translate('Continue')}
        onConfirm={onLayoutChangeConfirm}
        onCancel={onLayoutChangeCancel}
      />

      <ConfirmModal
        isOpen={isDeleteModalOpen}
        kind={kinds.DANGER}
        title={translate('RemoveRootFolder')}
        message={translate('RemoveRootFolderMoviesMessageText', { path })}
        confirmLabel={translate('Remove')}
        onConfirm={onConfirmDelete}
        onCancel={onDeleteModalClose}
      />
    </TableRow>
  );
}

export default RootFolderRow;
